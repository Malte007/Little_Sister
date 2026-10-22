using LittleSister.Core.Cameras;
using LittleSister.Core.Events;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Onvif;
using LittleSister.Infrastructure.Recording;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LittleSister.Infrastructure.Events;

/// <summary>
/// Hört für jede Kamera mit ONVIF-Adresse auf Bewegungsmeldungen und speichert sie als Ereignisse.
/// Die Ereignisse erscheinen auf der Zeitleiste; im Modus "Bei Bewegung" starten und stoppen sie
/// über <see cref="MotionSignals"/> die Aufnahme.
/// </summary>
public sealed class MotionEventService(
    IServiceScopeFactory scopes,
    CameraChanges changes,
    IMotionSource source,
    CameraStatusRegistry statuses,
    MotionSignals signals,
    TimeProvider time,
    ILogger<MotionEventService> logger) : BackgroundService
{
    /// <summary>Beginnt die nächste Bewegung kurz nach dem Ende der letzten, wird daraus ein Ereignis.</summary>
    public static readonly TimeSpan MergeGap = TimeSpan.FromSeconds(10);

    // Wartezeiten vor einem neuen Verbindungsversuch
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5)];

    private readonly System.Threading.Channels.ChannelReader<bool> reload = changes.Subscribe();
    private readonly Dictionary<Guid, (string Fingerprint, CancellationTokenSource Cts, Task Task)> running = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await CloseDanglingAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Abgleich der Ereignisquellen fehlgeschlagen");
                }
                await CameraChanges.WaitAsync(reload, TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            await Task.WhenAll(running.Keys.ToList().Select(StopAsync));
        }
    }

    private async Task SyncAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();
        var cameras = await db.Cameras.AsNoTracking()
            .Where(c => c.OnvifAddress != null && c.RecordingMode != RecordingMode.Off)
            .ToListAsync(ct);
        var wanted = cameras.ToDictionary(c => c.Id, c => (Camera: c, Fingerprint: string.Join('|', c.OnvifAddress, c.Username, c.ProtectedPassword)));

        foreach (var id in running.Keys.Where(id => !wanted.TryGetValue(id, out var w) || w.Fingerprint != running[id].Fingerprint).ToList())
            await StopAsync(id);

        foreach (var (id, (camera, fingerprint)) in wanted)
        {
            if (running.ContainsKey(id))
                continue;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            running[id] = (fingerprint, cts, Task.Run(() => WatchAsync(camera, cts.Token), CancellationToken.None));
        }
    }

    private async Task StopAsync(Guid id)
    {
        if (!running.Remove(id, out var entry))
            return;
        await entry.Cts.CancelAsync();
        try { await entry.Task; }
        catch (Exception ex) { logger.LogError(ex, "Ereignisquelle {Camera} mit Fehler beendet", id); }
        entry.Cts.Dispose();
        statuses.Update(id, s => s with { Events = EventState.None, EventError = null, Motion = false });
    }

    /// <summary>Hält die Verbindung zu einer Kamera und baut sie nach Fehlern wieder auf.</summary>
    private async Task WatchAsync(Camera camera, CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            statuses.Update(camera.Id, s => s with { Events = s.Events == EventState.Error ? EventState.Error : EventState.Connecting });
            try
            {
                await foreach (var motion in source.WatchAsync(camera, ct))
                {
                    attempt = 0;
                    var now = time.GetUtcNow();
                    statuses.Update(camera.Id, s => s with
                    {
                        Events = EventState.Connected, EventError = null, Motion = motion,
                        LastMotionUtc = motion ? now : s.LastMotionUtc,
                    });
                    signals.Set(camera.Id, motion, now);
                    logger.LogDebug("Bewegung {Camera}: {State}", camera.Name, motion ? "ja" : "nein");
                    if (motion)
                        await BeginAsync(camera.Id, now, ct);
                    else
                        await EndAsync(camera.Id, now, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var code = ex is OnvifException onvif ? onvif.Code : "eventsFailed";
                statuses.Update(camera.Id, s => s with { Events = EventState.Error, EventError = code, Motion = false });
                // Nur der erste Fehler einer Reihe ist eine Warnung wert, sonst füllt eine Kamera ohne Ereignisse das Protokoll
                logger.Log(attempt == 0 ? LogLevel.Warning : LogLevel.Debug, ex, "Ereignisse von {Camera} nicht verfügbar: {Error}", camera.Name, ex.Message);
            }
            finally
            {
                // Ohne Verbindung wissen wir nicht, ob die Bewegung andauert: Ereignis jetzt beenden
                signals.Set(camera.Id, false, time.GetUtcNow());
                await EndAsync(camera.Id, time.GetUtcNow(), CancellationToken.None);
            }

            try
            {
                await Task.Delay(Backoff[Math.Min(attempt++, Backoff.Length - 1)], time, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Bewegung beginnt: neues Ereignis, oder das gerade beendete fortsetzen.</summary>
    internal async Task BeginAsync(Guid cameraId, DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();
        var last = await db.Events.Where(e => e.CameraId == cameraId && e.Kind == CameraEvent.Motion)
            .OrderByDescending(e => e.StartUtc).FirstOrDefaultAsync(ct);

        if (last is { EndUtc: null })
            return;
        if (last is { EndUtc: { } end } && now - end <= MergeGap)
            last.EndUtc = null;
        else
            db.Events.Add(new CameraEvent { CameraId = cameraId, Kind = CameraEvent.Motion, StartUtc = now });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Bewegung endet: offene Ereignisse der Kamera abschließen.</summary>
    internal async Task EndAsync(Guid cameraId, DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();
        var open = await db.Events.Where(e => e.CameraId == cameraId && e.EndUtc == null).ToListAsync(ct);
        foreach (var e in open)
            e.EndUtc = now < e.StartUtc ? e.StartUtc : now;
        if (open.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Nach einem Neustart offene Ereignisse schließen: Sie enden mit der letzten Aufnahme der Kamera,
    /// denn danach lief Little Sister nicht mehr.
    /// </summary>
    private async Task CloseDanglingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();
        foreach (var e in await db.Events.Where(e => e.EndUtc == null).ToListAsync(ct))
        {
            var lastSegment = await db.Segments.Where(s => s.CameraId == e.CameraId)
                .OrderByDescending(s => s.StartUtc).Select(s => (DateTimeOffset?)s.EndUtc).FirstOrDefaultAsync(ct);
            e.EndUtc = lastSegment is { } end && end > e.StartUtc ? end : e.StartUtc;
        }
        await db.SaveChangesAsync(ct);
    }
}
