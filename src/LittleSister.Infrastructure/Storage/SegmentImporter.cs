using System.Diagnostics;
using System.Globalization;
using LittleSister.Core.Recording;
using LittleSister.Core.Settings;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Playback;
using LittleSister.Infrastructure.Recording;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LittleSister.Infrastructure.Storage;

/// <summary>
/// Liest beim Start Segmentdateien ein, die im Kameraordner liegen, aber in der Datenbank fehlen
/// (z. B. nach einem Fehler oder einer wiederhergestellten Datenbank). Läuft im Hintergrund, die Oberfläche ist sofort da.
/// </summary>
public sealed class SegmentImporter(
    IServiceScopeFactory scopes,
    ISegmentDurationProbe durations,
    SegmentThumbnails thumbnails,
    ILogger<SegmentImporter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Warten, bis die Einrichtung abgeschlossen und der Aufnahmeordner erreichbar ist; danach genügt ein Durchlauf
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await RunOnceAsync(stoppingToken) is not null)
                    return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Einlesen unbekannter Aufnahmen fehlgeschlagen");
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    /// <summary>Ein Durchlauf über alle bekannten Kameras und Aufnahmeordner.</summary>
    /// <returns>Anzahl eingelesener Segmente, oder null, wenn der Aufnahmeordner (noch) nicht bereit ist.</returns>
    public async Task<int?> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();

        var settings = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == SystemSettings.SingletonId, ct);
        if (string.IsNullOrEmpty(settings?.RecordingsPath) || !await StorageMarker.IsAvailableAsync(db, settings.RecordingsPath, ct))
            return null;

        // Auch frühere Aufnahmeordner durchsuchen, sofern sie noch erreichbar sind
        var roots = await db.Segments.Select(s => s.StorageRoot).Distinct().ToListAsync(ct);
        roots.Add(settings.RecordingsPath);
        var cameraIds = await db.Cameras.Select(c => c.Id).ToListAsync(ct);

        var imported = 0;
        foreach (var root in roots.Distinct())
        {
            if (root != settings.RecordingsPath && !await StorageMarker.IsAvailableAsync(db, root, ct))
                continue;
            foreach (var cameraId in cameraIds)
                imported += await ImportCameraAsync(db, root, cameraId, ct);
        }

        if (imported > 0)
            logger.LogInformation("{Count} unbekannte Aufnahme(n) eingelesen", imported);
        return imported;
    }

    private async Task<int> ImportCameraAsync(LittleSisterDbContext db, string root, Guid cameraId, CancellationToken ct)
    {
        var known = (await db.Segments.Where(s => s.CameraId == cameraId && s.StorageRoot == root)
            .Select(s => s.RelativePath).ToListAsync(ct)).ToHashSet();

        var imported = 0;
        foreach (var (file, start) in FindSegmentFiles(SegmentFiles.CameraFolder(root, cameraId)))
        {
            var relative = SegmentFiles.FinalRelativePath(cameraId, start);
            if (known.Contains(relative))
                continue;

            // Gerade erst fertig gewordene Segmente trägt der Recorder selbst ein
            var info = new FileInfo(file);
            if (info.LastWriteTimeUtc > DateTime.UtcNow - TimeSpan.FromMinutes(2)
                || await db.Segments.AnyAsync(s => s.CameraId == cameraId && s.StorageRoot == root && s.RelativePath == relative, ct))
                continue;

            var duration = await durations.GetDurationAsync(file, ct);
            if (duration is not { } d || d <= TimeSpan.Zero || !info.Exists || info.Length == 0)
            {
                logger.LogWarning("Aufnahme {File} ist nicht lesbar und wird übersprungen", file);
                continue;
            }

            var segment = new Segment
            {
                CameraId = cameraId,
                StartUtc = start,
                EndUtc = start + d,
                StorageRoot = root,
                RelativePath = relative,
                SizeBytes = info.Length,
                InitBytes = Mp4Boxes.FindInitSize(file) ?? 0,
            };
            db.Segments.Add(segment);
            await db.SaveChangesAsync(ct);
            thumbnails.Enqueue(segment);
            imported++;
        }
        return imported;
    }

    /// <summary>Segmentdateien nach dem Schema &lt;JJJJMMTT&gt;/&lt;HHMMSS&gt;.mp4; Eingangs- und Pufferordner passen nicht und bleiben außen vor.</summary>
    internal static IEnumerable<(string File, DateTimeOffset StartUtc)> FindSegmentFiles(string cameraFolder)
    {
        if (!Directory.Exists(cameraFolder))
            yield break;

        foreach (var dayFolder in Directory.EnumerateDirectories(cameraFolder).Order())
        {
            var day = Path.GetFileName(dayFolder);
            foreach (var file in Directory.EnumerateFiles(dayFolder, "*.mp4").Order())
            {
                if (DateTimeOffset.TryParseExact(day + Path.GetFileNameWithoutExtension(file), "yyyyMMddHHmmss",
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start))
                    yield return (file, start);
            }
        }
    }
}

/// <summary>Länge einer Aufnahme ermitteln; austauschbar für Tests.</summary>
public interface ISegmentDurationProbe
{
    /// <returns>Länge, oder null, wenn die Datei nicht lesbar ist.</returns>
    Task<TimeSpan?> GetDurationAsync(string file, CancellationToken ct);
}

/// <summary>Länge per ffprobe aus dem MP4-Container.</summary>
public sealed class FfprobeDurationProbe(IOptions<LittleSisterOptions> options) : ISegmentDurationProbe
{
    public async Task<TimeSpan?> GetDurationAsync(string file, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(options.Value.Recording.FfprobePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", file })
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        _ = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            return null;
        }

        return process.ExitCode == 0 && double.TryParse((await stdout).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }
}
