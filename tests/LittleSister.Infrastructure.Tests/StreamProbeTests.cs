using System.Diagnostics;
using LittleSister.Infrastructure.Recording;
using Microsoft.Extensions.Options;

namespace LittleSister.Infrastructure.Tests;

public class StreamProbeTests
{
    private static StreamProbe Probe(string ffmpeg = "ffmpeg") =>
        new(Options.Create(new LittleSisterOptions { Recording = { FfmpegPath = ffmpeg } }));

    /// <summary>Mit echtem ffmpeg: aus einem kurzen Testvideo entsteht ein verkleinertes JPEG.</summary>
    [Fact]
    public async Task Standbild_LiefertJpeg()
    {
        var directory = Directory.CreateTempSubdirectory("ls-snapshot-").FullName;
        try
        {
            var file = Path.Combine(directory, "test.mp4");
            var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true };
            foreach (var arg in new[] { "-y", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=s=1280x720:r=10:d=1", "-c:v", "libx264", "-pix_fmt", "yuv420p", file })
                psi.ArgumentList.Add(arg);
            using (var process = Process.Start(psi)!)
            {
                await process.WaitForExitAsync();
                Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
            }

            var (jpeg, error) = await Probe().SnapshotAsync(["-i", file], CancellationToken.None);

            Assert.Null(error);
            Assert.NotNull(jpeg);
            // JPEG beginnt mit FF D8
            Assert.Equal([0xFF, 0xD8], jpeg[..2]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Standbild_OhneFfmpeg_Fehlercode()
    {
        var (jpeg, error) = await Probe("/nicht/vorhanden/ffmpeg").SnapshotAsync(["-i", "egal.mp4"], CancellationToken.None);
        Assert.Null(jpeg);
        Assert.Equal("ffmpegMissing", error);
    }

    [Fact]
    public void StandbildArgumente_EinVerkleinertesBild()
    {
        var args = StreamProbe.BuildSnapshotArguments(["-i", "rtsp://kamera/12"]);
        Assert.Contains("1", args);
        Assert.Contains($"scale={StreamProbe.SnapshotWidth}:-2", args);
        Assert.Equal("pipe:1", args[^1]);
    }
}
