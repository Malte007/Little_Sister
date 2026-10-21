using LittleSister.Core.Recording;
using LittleSister.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LittleSister.Infrastructure.Recording;

/// <summary>Verschiebt fertige Segmente an ihren Ablageort und trägt sie in die Datenbank ein.</summary>
public sealed class SegmentIndexer(IServiceScopeFactory scopes, ILogger<SegmentIndexer> logger)
{
    /// <summary>Übernimmt ein fertig geschriebenes Segment aus dem Eingangsordner.</summary>
    /// <param name="duration">Länge laut ffmpeg; null = aus Dateizeit schätzen (z. B. nach einem Absturz).</param>
    public async Task<Segment?> CommitAsync(string root, Guid cameraId, string incomingFile, TimeSpan? duration, CancellationToken ct)
    {
        if (!File.Exists(incomingFile) || !SegmentFiles.TryParseStart(incomingFile, out var start))
            return null;

        var info = new FileInfo(incomingFile);
        if (info.Length == 0)
        {
            File.Delete(incomingFile); // Verbindung brach sofort ab, nichts aufgenommen
            return null;
        }

        var end = duration is { } d ? start + d : new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
        if (end <= start)
            end = start.AddSeconds(1);

        var relative = SegmentFiles.FinalRelativePath(cameraId, start);
        var target = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(incomingFile, target, overwrite: true);

        var segment = new Segment
        {
            CameraId = cameraId,
            StartUtc = start,
            EndUtc = end,
            RelativePath = relative,
            SizeBytes = info.Length,
        };

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();
        db.Segments.Add(segment);
        await db.SaveChangesAsync(ct);
        return segment;
    }

    /// <summary>Übernimmt Reste im Eingangsordner, die nach einem Absturz liegen geblieben sind.</summary>
    public async Task RecoverIncomingAsync(string root, Guid cameraId, CancellationToken ct)
    {
        var incoming = SegmentFiles.IncomingDirectory(root, cameraId);
        if (!Directory.Exists(incoming))
            return;

        foreach (var file in Directory.EnumerateFiles(incoming, "*.mp4").Order())
        {
            try
            {
                if (await CommitAsync(root, cameraId, file, duration: null, ct) is { } segment)
                    logger.LogInformation("Unvollständiges Segment übernommen: {Path}", segment.RelativePath);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Segment {File} konnte nicht übernommen werden", file);
            }
        }
    }
}
