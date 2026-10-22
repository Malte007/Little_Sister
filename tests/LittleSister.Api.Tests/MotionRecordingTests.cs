using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using LittleSister.Core.Cameras;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Events;
using LittleSister.Infrastructure.Recording;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LittleSister.Api.Tests;

/// <summary>Ende-zu-Ende: Aufnahme bei Bewegung mit echtem ffmpeg und einer vom Test gesteuerten Bewegung.</summary>
public class MotionRecordingTests
{
    private sealed class LoopingFileInput(string file) : ICameraInputFactory
    {
        public IReadOnlyList<string> CreateInputArguments(Camera camera) => ["-re", "-stream_loop", "-1", "-i", file];
    }

    /// <summary>Bewegung auf Knopfdruck statt per ONVIF.</summary>
    private sealed class ManualMotion : IMotionSource
    {
        private readonly Channel<bool> changes = Channel.CreateUnbounded<bool>();

        public void Set(bool motion) => changes.Writer.TryWrite(motion);

        public async IAsyncEnumerable<bool> WatchAsync(Camera camera, [EnumeratorCancellation] CancellationToken ct)
        {
            yield return false;
            await foreach (var motion in changes.Reader.ReadAllAsync(ct))
                yield return motion;
        }
    }

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

    private static async Task<(LittleSisterFactory Factory, HttpClient Admin, Guid Id, ManualMotion Motion)> StartAsync(int preRoll, int postRoll)
    {
        var factory = new LittleSisterFactory();
        var video = CreateTestVideo(Path.Combine(factory.DataPath, "quelle"));
        var motion = new ManualMotion();
        factory.Settings["LittleSister:Recording:FfmpegPath"] = "ffmpeg";
        factory.Settings["LittleSister:Recording:SegmentSeconds"] = "1";
        factory.ConfigureServices = s => s
            .AddSingleton<ICameraInputFactory>(new LoopingFileInput(video))
            .AddSingleton<IMotionSource>(motion);

        var admin = await factory.CreateAdminClientAsync();
        var created = await admin.PostAsJsonAsync("/api/cameras", new
        {
            name = "Hof", mainStreamUrl = "rtsp://127.0.0.1/test", onvifAddress = "http://127.0.0.1/onvif/device_service",
            recordingMode = "Motion", preRollSeconds = preRoll, postRollSeconds = postRoll, quotaGb = 10,
        });
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        return (factory, admin, id, motion);
    }

    private static async Task<List<LittleSister.Core.Recording.Segment>> SegmentsAsync(LittleSisterFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>().Segments
            .Where(s => s.CameraId == id).OrderBy(s => s.StartUtc).ToListAsync();
    }

    private static async Task<string?> StateAsync(HttpClient admin, Guid id) =>
        (await admin.GetFromJsonAsync<JsonElement>($"/api/cameras/{id}")).GetProperty("status").GetProperty("state").GetString();

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, message);
            await Task.Delay(250);
        }
    }

    [Fact]
    public async Task OhneVorlauf_NimmtNurWaehrendDerBewegungAuf()
    {
        var (factory, admin, id, motion) = await StartAsync(preRoll: 0, postRoll: 1);
        using var _ = factory;

        await WaitUntilAsync(async () => await StateAsync(admin, id) == "Standby", "Kamera wartet nicht auf Bewegung");
        await Task.Delay(2000);
        Assert.Empty(await SegmentsAsync(factory, id));

        var motionStart = DateTimeOffset.UtcNow;
        motion.Set(true);
        await WaitUntilAsync(async () => (await SegmentsAsync(factory, id)).Count >= 2, "Keine Aufnahme bei Bewegung");
        motion.Set(false);

        // Nach Bewegung und Nachlauf hört die Aufnahme auf
        await WaitUntilAsync(async () => await StateAsync(admin, id) == "Standby", "Aufnahme endet nicht");
        var count = (await SegmentsAsync(factory, id)).Count;
        await Task.Delay(2500);
        var segments = await SegmentsAsync(factory, id);
        Assert.Equal(count, segments.Count);
        Assert.All(segments, s => Assert.True(s.StartUtc >= motionStart.AddSeconds(-1)));

        using var scope = factory.Services.CreateScope();
        var e = Assert.Single(await scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>().Events.Where(x => x.CameraId == id).ToListAsync());
        Assert.NotNull(e.EndUtc);
    }

    [Fact]
    public async Task MitVorlauf_UebernimmtSegmenteVorDerBewegung()
    {
        var (factory, admin, id, motion) = await StartAsync(preRoll: 3, postRoll: 0);
        using var _ = factory;

        // Der Stream läuft in den Zwischenspeicher, gespeichert wird nichts
        var buffer = SegmentFiles.BufferDirectory(factory.RecordingsPath, id);
        await WaitUntilAsync(() => Task.FromResult(Directory.Exists(buffer) && Directory.EnumerateFiles(buffer).Count() >= 2), "Zwischenspeicher bleibt leer");
        await Task.Delay(4000);
        Assert.Empty(await SegmentsAsync(factory, id));
        Assert.True(Directory.EnumerateFiles(buffer).Count() <= 4, "Zwischenspeicher wird nicht aufgeräumt");

        var motionStart = DateTimeOffset.UtcNow;
        motion.Set(true);
        await WaitUntilAsync(async () => (await SegmentsAsync(factory, id)).Count >= 2, "Vorlauf nicht übernommen");
        var first = (await SegmentsAsync(factory, id))[0];
        Assert.True(first.StartUtc < motionStart, "Erstes Segment liegt nicht vor der Bewegung");
        Assert.True(first.StartUtc >= motionStart.AddSeconds(-7), "Mehr als der Vorlauf übernommen");
        motion.Set(false);
    }
}
