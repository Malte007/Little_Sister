using LittleSister.Core.Recording;
using LittleSister.Core.Settings;
using LittleSister.Core.Storage;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LittleSister.Infrastructure.Storage;

/// <summary>
/// Ringspeicher: löscht regelmäßig die ältesten Segmente, wenn Aufbewahrungsdauer,
/// Kontingent einer Kamera oder die globale Freispeichergrenze überschritten sind.
/// </summary>
public sealed class RetentionService(
    IServiceScopeFactory scopes,
    IOptions<LittleSisterOptions> options,
    IFreeSpaceProvider freeSpace,
    TimeProvider time,
    ILogger<RetentionService> logger) : BackgroundService
{
    private const long Gb = 1024L * 1024 * 1024;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.Recording.RetentionIntervalSeconds), time);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Ringspeicher-Lauf fehlgeschlagen");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Ein Durchlauf; öffentlich, damit Tests ihn direkt auslösen können.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();

        var settings = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == SystemSettings.SingletonId, ct);
        if (string.IsNullOrEmpty(settings?.RecordingsPath))
            return 0;

        var root = settings.RecordingsPath;
        var deleted = 0;
        var now = time.GetUtcNow();

        foreach (var camera in await db.Cameras.AsNoTracking().ToListAsync(ct))
        {
            // 1) Maximale Aufbewahrung (Datenschutz)
            if (RetentionPolicy.RetentionCutoff(camera.MaxRetentionDays, now) is { } cutoff)
            {
                var expired = await db.Segments
                    .Where(s => s.CameraId == camera.Id && !s.IsProtected && s.EndUtc < cutoff)
                    .ToListAsync(ct);
                deleted += await DeleteAsync(db, expired, ct);
            }

            // 2) Kontingent der Kamera
            var used = await db.Segments.Where(s => s.CameraId == camera.Id).SumAsync(s => s.SizeBytes, ct);
            if (used > camera.QuotaBytes)
                deleted += await DeleteAsync(db, await OldestToFreeAsync(db, used - camera.QuotaBytes, camera.Id, ct), ct);
        }

        // 3) Globale Freispeichergrenze über alle Kameras
        var available = freeSpace.GetAvailableBytes(root);
        var required = (long)(settings.MinFreeGb * Gb);
        if (available is { } free && free < required)
        {
            logger.LogWarning("Nur noch {Free:F1} GB frei (Grenze {Min} GB); lösche älteste Aufnahmen", free / (double)Gb, settings.MinFreeGb);
            deleted += await DeleteAsync(db, await OldestToFreeAsync(db, required - free, null, ct), ct);
        }

        if (deleted > 0)
            logger.LogInformation("Ringspeicher: {Count} Segment(e) gelöscht", deleted);

        // 4) Ereignisse ohne Aufnahme mehr dazu: älter als die älteste Aufnahme oder die maximale Aufbewahrung
        foreach (var camera in await db.Cameras.AsNoTracking().ToListAsync(ct))
        {
            var oldest = await db.Segments.Where(s => s.CameraId == camera.Id)
                .OrderBy(s => s.StartUtc).Select(s => (DateTimeOffset?)s.StartUtc).FirstOrDefaultAsync(ct);
            var cutoff = RetentionPolicy.RetentionCutoff(camera.MaxRetentionDays, now);
            if ((oldest, cutoff) is (null, null))
                continue;
            var before = oldest is { } o && cutoff is { } c ? (o > c ? o : c) : (oldest ?? cutoff)!.Value;
            await db.Events.Where(e => e.CameraId == camera.Id && e.EndUtc != null && e.EndUtc < before).ExecuteDeleteAsync(ct);
        }
        return deleted;
    }

    /// <summary>Lädt die ältesten Segmente stapelweise, bis genug Platz zusammenkommt.</summary>
    private static async Task<IReadOnlyList<Segment>> OldestToFreeAsync(LittleSisterDbContext db, long bytesToFree, Guid? cameraId, CancellationToken ct)
    {
        var query = db.Segments.Where(s => !s.IsProtected);
        if (cameraId is { } id)
            query = query.Where(s => s.CameraId == id);

        // Ein Stapel von 2000 Segmenten reicht bei 1-Minuten-Segmenten für über einen Tag Aufnahmen
        var batch = await query.OrderBy(s => s.StartUtc).Take(2000).ToListAsync(ct);
        return RetentionPolicy.SelectToFree(batch, bytesToFree);
    }

    private async Task<int> DeleteAsync(LittleSisterDbContext db, IReadOnlyList<Segment> segments, CancellationToken ct)
    {
        if (segments.Count == 0)
            return 0;

        foreach (var segment in segments)
        {
            try
            {
                // Jedes Segment liegt in dem Ordner, der bei seiner Aufnahme galt
                File.Delete(segment.FullPath);
                File.Delete(SegmentThumbnails.PathOf(segment));
                DeleteEmptyDirectory(Path.GetDirectoryName(segment.FullPath)!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Datei {Path} konnte nicht gelöscht werden", segment.FullPath);
            }
        }

        db.Segments.RemoveRange(segments);
        await db.SaveChangesAsync(ct);
        return segments.Count;
    }

    /// <summary>Entfernt leere Tagesordner, damit sich keine Verzeichnisleichen ansammeln.</summary>
    private static void DeleteEmptyDirectory(string directory)
    {
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);
    }
}

/// <summary>Freier Speicherplatz eines Ordners; austauschbar für Tests.</summary>
public interface IFreeSpaceProvider
{
    long? GetAvailableBytes(string path);
}

public sealed class DriveFreeSpaceProvider : IFreeSpaceProvider
{
    public long? GetAvailableBytes(string path)
    {
        try
        {
            return Directory.Exists(path) ? new DriveInfo(Path.GetFullPath(path)).AvailableFreeSpace : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
