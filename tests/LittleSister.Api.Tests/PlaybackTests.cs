using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LittleSister.Core.Recording;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Playback;
using LittleSister.Infrastructure.Recording;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LittleSister.Api.Tests;

/// <summary>Wiedergabe und Export mit echten Segmenten, wie sie die Aufnahme schreibt.</summary>
public class PlaybackTests
{
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

    /// <summary>Schreibt drei 2-Sekunden-Segmente (H.264 + AAC, fragmentiert) und trägt sie ohne Kopfgröße ein.</summary>
    private static async Task<(Guid Camera, DateTimeOffset Start)> SeedAsync(LittleSisterFactory factory, HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/cameras", new
        {
            name = "Einfahrt/Tör", mainStreamUrl = "rtsp://192.168.1.20/s", recordingEnabled = false, quotaGb = 10,
        });
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var source = Path.Combine(factory.DataPath, "quelle");
        Directory.CreateDirectory(source);
        Run("ffmpeg", "-y", "-loglevel", "error",
            "-f", "lavfi", "-i", "testsrc=size=320x240:rate=10", "-f", "lavfi", "-i", "sine=frequency=440",
            "-t", "6", "-c:v", "libx264", "-g", "10", "-pix_fmt", "yuv420p", "-c:a", "aac",
            "-f", "segment", "-segment_time", "2", "-reset_timestamps", "1", "-segment_format", "mp4",
            "-segment_format_options", "movflags=+frag_keyframe+empty_moov+default_base_moof",
            Path.Combine(source, "%d.mp4"));

        // Eine Stunde zurück, damit der Tag eindeutig ist und nichts in die Zukunft reicht
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        start = new DateTimeOffset(start.Year, start.Month, start.Day, start.Hour, start.Minute, start.Second, TimeSpan.Zero);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();
        for (var i = 0; i < 3; i++)
        {
            var segmentStart = start.AddSeconds(2 * i);
            var relative = SegmentFiles.FinalRelativePath(id, segmentStart);
            var target = Path.Combine(factory.RecordingsPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(source, $"{i}.mp4"), target);
            db.Segments.Add(new Segment
            {
                CameraId = id, StorageRoot = factory.RecordingsPath, RelativePath = relative,
                StartUtc = segmentStart, EndUtc = segmentStart.AddSeconds(2), SizeBytes = new FileInfo(target).Length,
            });
        }
        await db.SaveChangesAsync();
        return (id, start);
    }

    private static string DateOf(DateTimeOffset utc) =>
        RecordingDay.DateOf(utc, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Tag_Playlist_UndSegmente_SindAbspielbar()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        var (camera, start) = await SeedAsync(factory, admin);
        var viewer = await factory.CreateUserClientAsync(admin, "gast", "Viewer");
        var date = DateOf(start);

        var day = await viewer.GetFromJsonAsync<JsonElement>($"/api/recordings/{camera}/day?date={date}");
        Assert.Equal(date, day.GetProperty("date").GetString());
        Assert.Equal("Europe/Berlin", day.GetProperty("timeZoneId").GetString());
        Assert.Equal(3, day.GetProperty("segments").GetArrayLength());
        Assert.Equal(0, day.GetProperty("events").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, day.GetProperty("previousDate").ValueKind);

        // Ohne Datum: heute
        var today = await viewer.GetFromJsonAsync<JsonElement>($"/api/recordings/{camera}/day");
        Assert.Equal(DateOf(DateTimeOffset.UtcNow), today.GetProperty("date").GetString());

        var playlist = await viewer.GetStringAsync($"/api/recordings/{camera}/day/{date}/index.m3u8");
        Assert.Equal(3, playlist.Split('\n').Count(l => l.StartsWith("#EXTINF:2.000", StringComparison.Ordinal)));
        Assert.EndsWith("#EXT-X-ENDLIST\n", playlist);

        // Die Kopfgröße wurde beim ersten Abruf ermittelt und gespeichert
        using (var scope = factory.Services.CreateScope())
            Assert.All(await scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>().Segments.ToListAsync(), s => Assert.True(s.InitBytes > 0));

        // Segment per Byte-Bereich laden, wie es der Player tut
        var map = playlist.Split('\n').First(l => l.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal));
        var url = map.Split('"')[1];
        var request = new HttpRequestMessage(HttpMethod.Get, url) { Headers = { Range = new RangeHeaderValue(0, 7) } };
        var partial = await viewer.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString((await partial.Content.ReadAsByteArrayAsync())[4..8]));

        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/recordings/segments/999999.mp4")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/recordings/{Guid.NewGuid()}/day")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/recordings/{camera}/day")).StatusCode);
    }

    [Fact]
    public async Task Export_SetztZeitraumOhneUmwandlungZusammen()
    {
        using var factory = new LittleSisterFactory();
        factory.Settings["LittleSister:Recording:FfmpegPath"] = "ffmpeg";
        var admin = await factory.CreateAdminClientAsync();
        var (camera, start) = await SeedAsync(factory, admin);

        string Query(DateTimeOffset from, DateTimeOffset to) =>
            $"/api/recordings/{camera}/export?fromUtc={Uri.EscapeDataString(from.ToString("O"))}&toUtc={Uri.EscapeDataString(to.ToString("O"))}";

        var response = await admin.GetAsync(Query(start.AddSeconds(1), start.AddSeconds(5)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("Einfahrt_Tör_", response.Content.Headers.ContentDisposition?.FileNameStar);

        var file = Path.Combine(factory.DataPath, "export.mp4");
        await File.WriteAllBytesAsync(file, await response.Content.ReadAsByteArrayAsync());
        var probe = Run("ffprobe", "-v", "error", "-show_entries", "stream=codec_type:format=duration", "-of", "json", file);
        var info = JsonDocument.Parse(probe).RootElement;
        var duration = double.Parse(info.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        // Beginnt am Schlüsselbild davor, also 4–5 s statt genau 4 s
        Assert.InRange(duration, 3.5, 5.5);
        var types = info.GetProperty("streams").EnumerateArray().Select(s => s.GetProperty("codec_type").GetString()).ToList();
        Assert.Contains("video", types);
        Assert.Contains("audio", types);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(Query(start, start.AddSeconds(-1)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(Query(start, start.AddHours(25)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(Query(start.AddDays(-2), start.AddDays(-1)))).StatusCode);
    }

    [Fact]
    public async Task Vorschaubilder_EntstehenBeiBedarfUndWerdenZwischengespeichert()
    {
        using var factory = new LittleSisterFactory();
        factory.Settings["LittleSister:Recording:FfmpegPath"] = "ffmpeg";
        var admin = await factory.CreateAdminClientAsync();
        var (camera, start) = await SeedAsync(factory, admin);

        var day = await admin.GetFromJsonAsync<JsonElement>($"/api/recordings/{camera}/day?date={DateOf(start)}");
        Assert.Equal(SegmentThumbnails.IntervalSeconds, day.GetProperty("thumbnailSeconds").GetInt32());
        var segment = day.GetProperty("segments")[0];
        Assert.Equal(1, segment.GetProperty("thumbnails").GetInt32());

        var url = $"/api/recordings/segments/{segment.GetProperty("id").GetInt64()}/thumbnails.jpg";
        var response = await admin.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xFF, 0xD8], bytes[..2]);

        // Liegt jetzt neben dem Segment und wird beim nächsten Mal nur ausgeliefert
        var files = Directory.EnumerateFiles(factory.RecordingsPath, "*.jpg", SearchOption.AllDirectories).ToList();
        Assert.Single(files);
        Assert.Equal(bytes, await (await admin.GetAsync(url)).Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/recordings/segments/999999/thumbnails.jpg")).StatusCode);
    }
}
