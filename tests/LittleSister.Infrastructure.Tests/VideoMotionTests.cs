using System.Diagnostics;
using LittleSister.Core.Cameras;
using LittleSister.Infrastructure.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LittleSister.Infrastructure.Tests;

public class VideoMotionTests
{
    private const int Size = VideoMotionSource.Width * VideoMotionSource.Height;

    private static byte[] Gray(byte value) => Enumerable.Repeat(value, Size).ToArray();

    /// <summary>Graues Bild mit hellem Quadrat (Seitenlänge in Pixeln) an Position x.</summary>
    private static byte[] WithBox(int x, int side)
    {
        var frame = Gray(100);
        for (var row = 10; row < 10 + side; row++)
            for (var col = x; col < x + side; col++)
                frame[row * VideoMotionSource.Width + col] = 220;
        return frame;
    }

    [Fact]
    public void RuhigesBild_KeineBewegung()
    {
        var detector = new MotionDetector(50, 5);
        for (var i = 0; i < 20; i++)
            Assert.Null(detector.Process(Gray(100)));
        Assert.False(detector.Active);
    }

    [Fact]
    public void BewegtesObjekt_StartetUndEndetNachRuhe()
    {
        var detector = new MotionDetector(50, 5);
        detector.Process(Gray(100));

        // Ein Bild allein genügt nicht (Störung), ab dem zweiten in Folge beginnt die Bewegung
        Assert.Null(detector.Process(WithBox(0, 12)));
        Assert.True(detector.Process(WithBox(15, 12)));

        // Danach Ruhe: Ende erst nach 3 s (15 Bilder bei 5 Bildern/s)
        var results = Enumerable.Range(0, 15).Select(_ => detector.Process(Gray(100))).ToList();
        Assert.All(results.Take(14), r => Assert.Null(r));
        Assert.False(results[^1]);
    }

    [Fact]
    public void Lichtwechsel_IstKeineBewegung()
    {
        var detector = new MotionDetector(100, 5);
        detector.Process(Gray(60));
        for (var i = 0; i < 5; i++)
            Assert.Null(detector.Process(Gray(200)));
        Assert.False(detector.Active);
    }

    [Theory]
    [InlineData(4, 50, false)]  // 16 von 4800 Pixeln = 0,3 %: zu klein für mittlere Empfindlichkeit
    [InlineData(4, 100, true)]  // reicht bei höchster Empfindlichkeit
    [InlineData(12, 50, true)]  // 144 Pixel = 3 %
    [InlineData(12, 1, false)]  // grobste Stufe verlangt 10 %
    public void Empfindlichkeit_BestimmtMindestgroesse(int side, int sensitivity, bool expected)
    {
        var detector = new MotionDetector(sensitivity, 5);
        detector.Process(Gray(100));
        detector.Process(WithBox(0, side));
        detector.Process(WithBox(20, side));
        Assert.Equal(expected, detector.Active);
    }

    [Fact]
    public void Schwelle_FaelltMitEmpfindlichkeit()
    {
        Assert.Equal(0.1, MotionDetector.AreaThreshold(1), 3);
        Assert.Equal(0.002, MotionDetector.AreaThreshold(100), 4);
        Assert.InRange(MotionDetector.AreaThreshold(50), 0.012, 0.016);
    }

    private sealed class FileInput(string file) : IMotionInputFactory
    {
        public IReadOnlyList<string> CreateInputArguments(Camera camera) => ["-re", "-i", file];
    }

    /// <summary>Ende-zu-Ende mit echtem ffmpeg: graues Bild, von 1,5 s bis 3,5 s fährt ein Quadrat durch.</summary>
    [Fact]
    public async Task Ffmpeg_ErkenntBewegungImVideo()
    {
        var directory = Directory.CreateTempSubdirectory("ls-motion-").FullName;
        try
        {
            var file = Path.Combine(directory, "bewegung.mp4");
            var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true };
            foreach (var arg in new[]
            {
                "-y", "-loglevel", "error",
                "-f", "lavfi", "-i", "color=c=gray:s=320x240:r=10:d=7",
                "-f", "lavfi", "-i", "color=c=white:s=60x60:r=10:d=7",
                "-filter_complex", "[0][1]overlay=x='if(between(t,1.5,3.5),(t-1.5)*120,-100)':y=90",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", file,
            })
                psi.ArgumentList.Add(arg);
            using (var process = Process.Start(psi)!)
            {
                await process.WaitForExitAsync();
                Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
            }

            var source = new VideoMotionSource(new FileInput(file),
                Options.Create(new LittleSisterOptions { Recording = { FfmpegPath = "ffmpeg" } }), NullLogger<VideoMotionSource>.Instance);
            var states = new List<bool>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            // Am Ende des Videos beendet sich ffmpeg; das meldet die Quelle als Fehler
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var motion in source.WatchAsync(new Camera { Name = "Test" }, timeout.Token))
                    states.Add(motion);
            });

            Assert.Equal([false, true, false], states);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FfmpegArgumente_KleineGraustufenbilder()
    {
        var args = VideoMotionSource.BuildArguments(["-i", "rtsp://kamera/12"]);
        Assert.Equal("rtsp://kamera/12", args[args.ToList().IndexOf("-i") + 1]);
        Assert.Contains("fps=5,scale=80:60:flags=area,format=gray", args);
        Assert.Equal("pipe:1", args[^1]);
    }
}
