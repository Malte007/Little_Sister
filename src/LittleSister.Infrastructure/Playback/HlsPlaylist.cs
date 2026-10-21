using System.Globalization;
using System.Text;

namespace LittleSister.Infrastructure.Playback;

/// <summary>Ein Segment, wie es in der Playlist erscheint.</summary>
public sealed record PlaylistSegment(long Id, DateTimeOffset StartUtc, DateTimeOffset EndUtc, int InitBytes, long SizeBytes)
{
    public double Seconds => Math.Max((EndUtc - StartUtc).TotalSeconds, 0.001);
}

/// <summary>
/// Baut eine HLS-Playlist (fMP4) direkt aus den Aufnahmesegmenten, ohne sie umzuwandeln:
/// Jede Datei liefert ihren Kopf als EXT-X-MAP und den Rest als Mediensegment (Byte-Bereiche).
/// Lücken zwischen den Dateien werden übersprungen; die Uhrzeit steht je Segment in EXT-X-PROGRAM-DATE-TIME.
/// </summary>
public static class HlsPlaylist
{
    public static string Build(IReadOnlyList<PlaylistSegment> segments, Func<long, string> segmentUrl)
    {
        var target = segments.Count == 0 ? 1 : (int)Math.Ceiling(segments.Max(s => s.Seconds));
        var sb = new StringBuilder()
            .Append("#EXTM3U\n")
            .Append("#EXT-X-VERSION:7\n")
            .Append(CultureInfo.InvariantCulture, $"#EXT-X-TARGETDURATION:{target}\n")
            .Append("#EXT-X-PLAYLIST-TYPE:VOD\n")
            .Append("#EXT-X-INDEPENDENT-SEGMENTS\n");

        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            var url = segmentUrl(s.Id);
            // Jede Datei beginnt bei Zeitstempel 0 und hat ihren eigenen Kopf
            if (i > 0)
                sb.Append("#EXT-X-DISCONTINUITY\n");
            sb.Append(CultureInfo.InvariantCulture, $"#EXT-X-MAP:URI=\"{url}\",BYTERANGE=\"{s.InitBytes}@0\"\n")
              .Append(CultureInfo.InvariantCulture, $"#EXT-X-PROGRAM-DATE-TIME:{s.StartUtc.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss.fff'Z'}\n")
              .Append(CultureInfo.InvariantCulture, $"#EXTINF:{s.Seconds:0.000},\n")
              .Append(CultureInfo.InvariantCulture, $"#EXT-X-BYTERANGE:{s.SizeBytes - s.InitBytes}@{s.InitBytes}\n")
              .Append(url).Append('\n');
        }

        return sb.Append("#EXT-X-ENDLIST\n").ToString();
    }
}
