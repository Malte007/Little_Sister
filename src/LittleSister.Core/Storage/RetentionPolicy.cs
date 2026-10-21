using LittleSister.Core.Recording;

namespace LittleSister.Core.Storage;

/// <summary>
/// Regeln des Ringspeichers: entscheidet, welche Segmente gelöscht werden.
/// Gelöscht wird immer von alt nach neu; geschützte Segmente bleiben stehen.
/// </summary>
public static class RetentionPolicy
{
    /// <summary>Stichtag der maximalen Aufbewahrung: Segmente, die vorher enden, werden gelöscht.</summary>
    public static DateTimeOffset? RetentionCutoff(int? maxRetentionDays, DateTimeOffset nowUtc) =>
        maxRetentionDays is { } days ? nowUtc.AddDays(-days) : null;

    /// <summary>
    /// Wählt die ältesten ungeschützten Segmente, bis mindestens <paramref name="bytesToFree"/> frei werden.
    /// Genutzt für das Kontingent einer Kamera und für die globale Freispeichergrenze.
    /// </summary>
    public static IReadOnlyList<Segment> SelectToFree(IEnumerable<Segment> segments, long bytesToFree)
    {
        var delete = new List<Segment>();
        if (bytesToFree <= 0)
            return delete;

        long freed = 0;
        foreach (var segment in segments.Where(s => !s.IsProtected).OrderBy(s => s.StartUtc))
        {
            delete.Add(segment);
            freed += segment.SizeBytes;
            if (freed >= bytesToFree)
                break;
        }
        return delete;
    }

    /// <summary>
    /// Schätzt, wie viele Tage das Kontingent bei der bisherigen Datenrate reicht.
    /// Liefert null, solange zu wenig Aufnahmen für eine Schätzung vorliegen.
    /// </summary>
    public static double? EstimateDays(long usedBytes, TimeSpan recordedDuration, long quotaBytes)
    {
        if (usedBytes <= 0 || recordedDuration < TimeSpan.FromMinutes(5))
            return null;
        var bytesPerDay = usedBytes / recordedDuration.TotalDays;
        return quotaBytes / bytesPerDay;
    }
}
