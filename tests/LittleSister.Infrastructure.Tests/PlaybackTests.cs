using System.Diagnostics;
using LittleSister.Core.Recording;
using LittleSister.Infrastructure.Playback;
using LittleSister.Infrastructure.Recording;

namespace LittleSister.Infrastructure.Tests;

public class PlaybackTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Tagesgrenzen_BeachtenZeitumstellung()
    {
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        var normal = RecordingDay.Of(new DateOnly(2026, 10, 21), berlin);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 22, 0, 0, TimeSpan.Zero), normal.FromUtc);
        Assert.Equal(TimeSpan.FromHours(24), normal.ToUtc - normal.FromUtc);

        // Ende der Sommerzeit: 25 Stunden, Beginn: 23 Stunden
        var autumn = RecordingDay.Of(new DateOnly(2026, 10, 25), berlin);
        Assert.Equal(TimeSpan.FromHours(25), autumn.ToUtc - autumn.FromUtc);
        var spring = RecordingDay.Of(new DateOnly(2026, 3, 29), berlin);
        Assert.Equal(TimeSpan.FromHours(23), spring.ToUtc - spring.FromUtc);

        Assert.Equal(new DateOnly(2026, 10, 22), RecordingDay.DateOf(new DateTimeOffset(2026, 10, 21, 22, 30, 0, TimeSpan.Zero), berlin));
        Assert.Equal(TimeZoneInfo.Utc, RecordingDay.Zone("Gibt/Es/Nicht"));
    }

    [Fact]
    public void Playlist_LiefertKopfUndDatenAlsByteBereiche()
    {
        var playlist = HlsPlaylist.Build(
        [
            new PlaylistSegment(1, T0, T0.AddSeconds(60), 900, 10_900),
            new PlaylistSegment(2, T0.AddSeconds(120), T0.AddSeconds(179.5), 950, 5_950),
        ], id => $"/api/recordings/segments/{id}.mp4");

        Assert.StartsWith("#EXTM3U\n", playlist);
        Assert.Contains("#EXT-X-TARGETDURATION:60\n", playlist);
        Assert.Contains("#EXT-X-MAP:URI=\"/api/recordings/segments/1.mp4\",BYTERANGE=\"900@0\"\n", playlist);
        Assert.Contains("#EXT-X-BYTERANGE:10000@900\n", playlist);
        Assert.Contains("#EXT-X-PROGRAM-DATE-TIME:2026-10-21T10:02:00.000Z\n", playlist);
        Assert.Contains("#EXTINF:59.500,\n", playlist);
        // Nur zwischen den Dateien eine Unterbrechung, am Ende das Listenende
        Assert.Single(playlist.Split('\n'), l => l == "#EXT-X-DISCONTINUITY");
        Assert.EndsWith("#EXT-X-ENDLIST\n", playlist);
    }

    [Fact]
    public void ExportListe_BeschneidetErstesUndLetztesSegment()
    {
        Segment Seg(int minute, string root = "/rec") => new()
        {
            StorageRoot = root, RelativePath = $"cam/{minute}.mp4", StartUtc = T0.AddMinutes(minute), EndUtc = T0.AddMinutes(minute + 1),
        };

        var list = ClipExport.BuildConcatList([Seg(0, "/rec/Malte's"), Seg(1), Seg(2)], T0.AddSeconds(15.5), T0.AddMinutes(2).AddSeconds(30));

        Assert.Equal(
            "ffconcat version 1.0\n" +
            "file '/rec/Malte'\\''s/cam/0.mp4'\ninpoint 15.5\n" +
            "file '/rec/cam/1.mp4'\n" +
            "file '/rec/cam/2.mp4'\noutpoint 30\n", list);
    }

    [Fact]
    public void Mp4Kopf_EndetVorDemErstenFragment()
    {
        var dir = Path.Combine(Path.GetTempPath(), "littlesister-mp4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "frag.mp4");
            var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true };
            foreach (var arg in new[] { "-y", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=size=160x120:rate=10", "-t", "2",
                         "-c:v", "libx264", "-g", "10", "-pix_fmt", "yuv420p", "-movflags", "frag_keyframe+empty_moov+default_base_moof", file })
                psi.ArgumentList.Add(arg);
            using (var process = Process.Start(psi)!)
            {
                process.WaitForExit();
                Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
            }

            var init = Mp4Boxes.FindInitSize(file);
            Assert.NotNull(init);
            var bytes = File.ReadAllBytes(file);
            Assert.Equal("moof", System.Text.Encoding.ASCII.GetString(bytes, init!.Value + 4, 4));

            // Normale (nicht fragmentierte) Dateien und Müll liefern null
            Assert.Null(Mp4Boxes.FindInitSize(new MemoryStream([0, 0, 0, 8, (byte)'f', (byte)'t', (byte)'y', (byte)'p'])));
            Assert.Null(Mp4Boxes.FindInitSize(Path.Combine(dir, "fehlt.mp4")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(60, 6)]
    [InlineData(61, 7)]
    [InlineData(59.5, 6)]
    [InlineData(2, 1)]
    [InlineData(0, 1)]
    public void Vorschaubilder_EinesJeIntervall(double seconds, int expected)
    {
        var segment = new Segment { StartUtc = T0, EndUtc = T0.AddSeconds(seconds) };
        Assert.Equal(expected, SegmentThumbnails.CountOf(segment));
    }

    [Fact]
    public async Task Vorschaubilder_StreifenMitEinemBildJeIntervall()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ls-thumbs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // 35 s mit Schlüsselbild alle 4 s → 4 Bilder, je 160 × 120 (Seitenverhältnis 4:3)
            var file = Path.Combine(dir, "120000.mp4");
            Run("ffmpeg", "-y", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=10", "-t", "35",
                "-c:v", "libx264", "-g", "40", "-pix_fmt", "yuv420p",
                "-movflags", "frag_keyframe+empty_moov+default_base_moof", "-f", "mp4", file);
            var segment = new Segment { StorageRoot = dir, RelativePath = "120000.mp4", StartUtc = T0, EndUtc = T0.AddSeconds(35) };

            var thumbnails = new SegmentThumbnails(
                Microsoft.Extensions.Options.Options.Create(new LittleSisterOptions()),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SegmentThumbnails>.Instance);
            Assert.True(await thumbnails.EnsureAsync(segment));

            var size = Run("ffprobe", "-v", "error", "-show_entries", "stream=width,height", "-of", "csv=p=0", SegmentThumbnails.PathOf(segment));
            Assert.Equal("640,120", size.Trim());
            Assert.Equal(Path.Combine(dir, "120000.jpg"), SegmentThumbnails.PathOf(segment));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string Run(string tool, params string[] args)
    {
        var psi = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        return output;
    }
}
