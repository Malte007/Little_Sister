using System.Threading.Channels;
using LittleSister.Core.Cameras;
using LittleSister.Core.Settings;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LittleSister.Infrastructure.Recording;

/// <summary>
/// Startet und stoppt für jede aktive Kamera einen eigenen <see cref="CameraRecorder"/>.
/// Ändern sich Kameradaten, wird nur der betroffene Recorder neu gestartet.
/// </summary>
public sealed class RecordingSupervisor(
    IServiceScopeFactory scopes,
    CameraChanges changes,
    ICameraInputFactory input,
    SegmentIndexer indexer,
    CameraStatusRegistry statuses,
    MotionSignals motion,
    IOptions<LittleSisterOptions> options,
    TimeProvider time,
    ILoggerFactory loggers) : BackgroundService
{
    private readonly ILogger logger = loggers.CreateLogger<RecordingSupervisor>();
    private readonly ChannelReader<bool> reload = changes.Subscribe();
    private readonly Dictionary<Guid, Running> running = [];

    private sealed record Running(string Fingerprint, CancellationTokenSource Cts, Task Task);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Abgleich der Aufnahmen fehlgeschlagen");
                }

                // Auf Änderungen warten; spätestens alle 30 s abgleichen (z. B. nach der Ersteinrichtung)
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

        var settings = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == SystemSettings.SingletonId, ct);
        var root = settings?.RecordingsPath;
        var cameras = string.IsNullOrEmpty(root)
            ? []
            : await db.Cameras.AsNoTracking().Where(c => c.RecordingMode != RecordingMode.Off).ToListAsync(ct);

        var wanted = cameras.ToDictionary(c => c.Id, c => (Camera: c, Fingerprint: Fingerprint(c, root!)));

        foreach (var id in running.Keys.Where(id => !wanted.TryGetValue(id, out var w) || w.Fingerprint != running[id].Fingerprint).ToList())
            await StopAsync(id);

        foreach (var (id, (camera, fingerprint)) in wanted)
        {
            if (running.ContainsKey(id))
                continue;

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var recorder = new CameraRecorder(camera, root!, options.Value.Recording, input, indexer, statuses, motion, time,
                loggers.CreateLogger<CameraRecorder>());
            running[id] = new Running(fingerprint, cts, Task.Run(() => recorder.RunAsync(cts.Token), CancellationToken.None));
            logger.LogInformation("Aufnahme gestartet: {Camera}", camera.Name);
        }

        // Kameras ohne Aufnahme als "aus" führen, gelöschte vergessen
        var allIds = await db.Cameras.Select(c => c.Id).ToListAsync(ct);
        foreach (var id in allIds.Where(id => !running.ContainsKey(id)))
            statuses.Update(id, s => s with { State = CameraState.Idle });
    }

    private async Task StopAsync(Guid id)
    {
        if (!running.Remove(id, out var entry))
            return;
        await entry.Cts.CancelAsync();
        try
        {
            await entry.Task;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Recorder {Camera} ist mit Fehler beendet worden", id);
        }
        entry.Cts.Dispose();
    }

    /// <summary>Alles, was einen Neustart des Recorders erfordert.</summary>
    private static string Fingerprint(Camera c, string root) =>
        string.Join('|', root, c.MainStreamUrl, c.Username, c.ProtectedPassword, c.Rotation, c.RecordingMode, c.PreRollSeconds, c.PostRollSeconds);
}
