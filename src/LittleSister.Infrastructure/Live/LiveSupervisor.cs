using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using LittleSister.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LittleSister.Infrastructure.Live;

/// <summary>
/// Startet go2rtc als Kindprozess, hält dessen Streams mit den Kameras in Sync
/// und startet es nach einem Absturz neu. Die go2rtc-API lauscht nur auf 127.0.0.1;
/// der Browser erreicht die Streams ausschließlich über die angemeldete Little-Sister-API.
/// </summary>
public sealed partial class LiveSupervisor(
    IServiceScopeFactory scopes,
    CameraChanges changes,
    ILiveSourceFactory sources,
    Go2rtcClient go2rtc,
    LiveStatus status,
    IOptions<LittleSisterOptions> options,
    ILogger<LiveSupervisor> logger) : BackgroundService
{
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60)];
    private readonly ChannelReader<bool> reload = changes.Subscribe();

    private LiveOptions Live => options.Value.Live;

    private string PidFile => Path.Combine(options.Value.GetFullDataPath(), "go2rtc.pid");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        KillOrphan();
        var attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var started = DateTime.UtcNow;
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Win32Exception ex)
            {
                // Programm fehlt: Live-Ansicht aus, Aufnahme läuft unabhängig davon weiter
                status.Set(new LiveState(false, "go2rtcMissing"));
                logger.LogWarning("go2rtc nicht gefunden ({Path}), Live-Ansicht ist abgeschaltet: {Message}", Live.Go2rtcPath, ex.Message);
            }
            catch (Exception ex)
            {
                status.Set(new LiveState(false, "go2rtcFailed"));
                logger.LogError(ex, "go2rtc ist ausgefallen");
            }

            if (DateTime.UtcNow - started > TimeSpan.FromMinutes(5))
                attempt = 0;
            try
            {
                await Task.Delay(Backoff[Math.Min(attempt++, Backoff.Length - 1)], stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Live.Go2rtcPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // Konfiguration direkt als JSON: es entsteht keine Datei, und go2rtc speichert keine Zugangsdaten
        psi.ArgumentList.Add("-config");
        psi.ArgumentList.Add(BuildConfig(Live, options.Value.Recording.FfmpegPath));

        using var process = Process.Start(psi)!;
        await File.WriteAllTextAsync(PidFile, process.Id.ToString(), CancellationToken.None);
        var logs = Task.WhenAll(ForwardLogAsync(process.StandardOutput), ForwardLogAsync(process.StandardError));
        try
        {
            await WaitUntilAliveAsync(process, ct);
            status.Set(new LiveState(true));
            logger.LogInformation("go2rtc gestartet (API 127.0.0.1:{ApiPort}, WebRTC {WebRtcPort})", Live.ApiPort, Live.WebRtcPort);

            var exited = process.WaitForExitAsync(CancellationToken.None);
            while (!exited.IsCompleted)
            {
                try
                {
                    await SyncAsync(ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException)
                {
                    logger.LogWarning(ex, "Abgleich der Live-Streams fehlgeschlagen");
                }
                await Task.WhenAny(exited, CameraChanges.WaitAsync(reload, TimeSpan.FromSeconds(30), ct));
                ct.ThrowIfCancellationRequested();
            }

            status.Set(new LiveState(false, "go2rtcFailed"));
            logger.LogWarning("go2rtc wurde beendet (Code {Code}), Neustart folgt", process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            await logs;
            File.Delete(PidFile);
        }
    }

    /// <summary>Gleicht die go2rtc-Streams mit den Kameras ab: anlegen, ändern, entfernen.</summary>
    private async Task SyncAsync(CancellationToken ct)
    {
        Dictionary<string, string> wanted = [];
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();
            foreach (var camera in await db.Cameras.AsNoTracking().ToListAsync(ct))
            {
                wanted[LiveStreams.Name(camera.Id)] = sources.CreateSource(camera, camera.SubStreamUrl ?? camera.MainStreamUrl);
                if (camera.SubStreamUrl is not null)
                    wanted[LiveStreams.HdName(camera.Id)] = sources.CreateSource(camera, camera.MainStreamUrl);
            }
        }

        var current = await go2rtc.GetStreamsAsync(ct);
        foreach (var (name, source) in wanted.Where(w => !current.TryGetValue(w.Key, out var s) || s != w.Value))
        {
            // Quelle geändert: erst löschen, damit go2rtc laufende Verbindungen zur alten Adresse beendet
            if (current.ContainsKey(name))
                await go2rtc.DeleteStreamAsync(name, ct);
            await go2rtc.SetStreamAsync(name, source, ct);
        }
        foreach (var name in current.Keys.Where(n => !wanted.ContainsKey(n)))
            await go2rtc.DeleteStreamAsync(name, ct);
    }

    private async Task WaitUntilAliveAsync(Process process, CancellationToken ct)
    {
        for (var i = 0; i < 100; i++)
        {
            if (process.HasExited)
                throw new InvalidOperationException($"go2rtc beim Start beendet (Code {process.ExitCode}), Port {Live.ApiPort} oder {Live.WebRtcPort} belegt?");
            if (await go2rtc.IsAliveAsync(ct))
                return;
            await Task.Delay(100, ct);
        }
        throw new TimeoutException("go2rtc antwortet nicht");
    }

    private async Task ForwardLogAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync(CancellationToken.None) is { } line)
        {
            // Zugangsdaten aus Adressen entfernen, bevor sie im Protokoll landen
            line = Credentials().Replace(line, "://***@");
            if (line.Contains(" ERR ") || line.Contains(" WRN "))
                logger.LogWarning("go2rtc: {Line}", line);
            else
                logger.LogDebug("go2rtc: {Line}", line);
        }
    }

    /// <summary>Beendet ein go2rtc, das ein abgebrochener früherer Lauf hinterlassen hat (z. B. Debugger gestoppt).</summary>
    private void KillOrphan()
    {
        try
        {
            if (!File.Exists(PidFile) || !int.TryParse(File.ReadAllText(PidFile), out var pid))
                return;
            using var orphan = Process.GetProcessById(pid);
            if (orphan.ProcessName.Contains("go2rtc", StringComparison.OrdinalIgnoreCase))
            {
                orphan.Kill();
                orphan.WaitForExit(TimeSpan.FromSeconds(5));
                logger.LogInformation("Verwaistes go2rtc (PID {Pid}) beendet", pid);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or Win32Exception) { }
    }

    public static string BuildConfig(LiveOptions live, string ffmpegPath) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["api"] = new { listen = $"127.0.0.1:{live.ApiPort}" },
        // Kein ungeschützter RTSP-Restream und kein HomeKit-Port nach außen
        ["rtsp"] = new { listen = "" },
        ["srtp"] = new { listen = "" },
        // Keine fremden STUN-Server: im LAN reichen die lokalen Adressen, zusätzliche per Konfiguration
        ["webrtc"] = new { listen = $":{live.WebRtcPort}", candidates = live.WebRtcCandidates, ice_servers = Array.Empty<object>() },
        ["ffmpeg"] = new { bin = ffmpegPath },
        ["log"] = new { format = "text", level = "info" },
    });

    [GeneratedRegex("://[^/@\\s]+@")]
    private static partial Regex Credentials();
}
