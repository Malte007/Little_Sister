using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using LittleSister.Core.Cameras;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Recording;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LittleSister.Api.Tests;

/// <summary>Ende-zu-Ende: echtes ffmpeg nimmt eine Testquelle auf, Segmente landen in Ordner und Datenbank.</summary>
public class RecordingTests
{
    /// <summary>Ersetzt RTSP durch eine Endlosschleife über ein kleines H.264-Video.</summary>
    private sealed class LoopingFileInput(string file) : ICameraInputFactory
    {
        public IReadOnlyList<string> CreateInputArguments(Camera camera) => ["-re", "-stream_loop", "-1", "-i", file];
    }

    /// <summary>Erzeugt mit ffmpeg ein 2-Sekunden-Testvideo (H.264, Keyframe jede Sekunde).</summary>
    private static string CreateTestVideo(string directory)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "testbild.mp4");
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true };
        foreach (var arg in new[] { "-y", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=10", "-t", "2", "-c:v", "libx264", "-g", "10", "-pix_fmt", "yuv420p", file })
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        return file;
    }

    [Fact]
    public async Task Daueraufnahme_SchreibtUndIndexiertSegmente()
    {
        using var factory = new LittleSisterFactory();
        var video = CreateTestVideo(Path.Combine(factory.DataPath, "quelle"));
        factory.Settings["LittleSister:Recording:FfmpegPath"] = "ffmpeg";
        factory.Settings["LittleSister:Recording:SegmentSeconds"] = "1";
        factory.ConfigureServices = s => s.AddSingleton<ICameraInputFactory>(new LoopingFileInput(video));

        var admin = await factory.CreateAdminClientAsync();
        var created = await admin.PostAsJsonAsync("/api/cameras", new
        {
            name = "Testbild", mainStreamUrl = "rtsp://127.0.0.1/test", recordingEnabled = true, quotaGb = 10,
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Bis zu 20 s auf mindestens drei fertige Segmente warten
        var deadline = DateTime.UtcNow.AddSeconds(20);
        int count;
        do
        {
            await Task.Delay(500);
            using var scope = factory.Services.CreateScope();
            count = await scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>().Segments.CountAsync(s => s.CameraId == id);
        }
        while (count < 3 && DateTime.UtcNow < deadline);
        Assert.True(count >= 3, $"Nur {count} Segmente aufgenommen");

        var camera = await admin.GetFromJsonAsync<JsonElement>($"/api/cameras/{id}");
        Assert.Equal("Recording", camera.GetProperty("status").GetProperty("state").GetString());
        Assert.True(camera.GetProperty("storage").GetProperty("usedBytes").GetInt64() > 0);

        using (var scope = factory.Services.CreateScope())
        {
            var segments = await scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>().Segments.Where(s => s.CameraId == id).ToListAsync();
            Assert.All(segments, s =>
            {
                Assert.True(File.Exists(Path.Combine(factory.RecordingsPath, s.RelativePath)));
                Assert.True(s.EndUtc > s.StartUtc);
            });
        }
    }
}
