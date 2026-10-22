using System.Diagnostics;
using System.Runtime.CompilerServices;
using LittleSister.Core.Cameras;
using LittleSister.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LittleSister.Infrastructure.Events;

/// <summary>Liefert die ffmpeg-Eingabe für die Bilderkennung (austauschbar für Tests).</summary>
public interface IMotionInputFactory
{
    IReadOnlyList<string> CreateInputArguments(Camera camera);
}

/// <summary>Standard: der kleine Substream per RTSP über TCP, ersatzweise der Hauptstream.</summary>
public sealed class RtspMotionInputFactory(CameraCredentialProtector credentials) : IMotionInputFactory
{
    public IReadOnlyList<string> CreateInputArguments(Camera camera) =>
    [
        "-rtsp_transport", "tcp",
        "-timeout", "10000000",
        "-i", StreamUrl.WithCredentials(camera.SubStreamUrl ?? camera.MainStreamUrl, camera.Username, credentials.Unprotect(camera.ProtectedPassword)),
    ];
}

/// <summary>
/// Bewegung aus dem Bild: ffmpeg dekodiert den Substream und liefert winzige Graustufenbilder,
/// <see cref="MotionDetector"/> vergleicht sie. Die CPU-Last steckt fast ganz im Dekodieren.
/// </summary>
public sealed class VideoMotionSource(
    IMotionInputFactory input, IOptions<LittleSisterOptions> options, ILogger<VideoMotionSource> logger) : IMotionSource
{
    /// <summary>Größe und Rate der Analysebilder; genügt für Personen und Fahrzeuge.</summary>
    public const int Width = 80, Height = 60, FramesPerSecond = 5;

    public async IAsyncEnumerable<bool> WatchAsync(Camera camera, [EnumeratorCancellation] CancellationToken ct)
    {
        var psi = new ProcessStartInfo(options.Value.Recording.FfmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in BuildArguments(input.CreateInputArguments(camera)))
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"ffmpeg nicht gefunden ({psi.FileName}): {ex.Message}", ex);
        }

        string? lastError = null;
        var stderr = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(CancellationToken.None) is { } line)
                lastError = line;
        }, CancellationToken.None);

        await using var stop = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* bereits beendet */ }
        });

        try
        {
            var detector = new MotionDetector(camera.MotionSensitivity, FramesPerSecond);
            var frame = new byte[Width * Height];
            var stream = process.StandardOutput.BaseStream;
            var first = true;
            while (await ReadFrameAsync(stream, frame))
            {
                var changed = detector.Process(frame);
                if (first)
                {
                    // Erstes Bild: Verbindung steht, Ausgangszustand melden
                    first = false;
                    logger.LogDebug("Bilderkennung {Camera} gestartet", camera.Name);
                    yield return false;
                }
                else if (changed is { } active)
                {
                    logger.LogDebug("Bilderkennung {Camera}: {Change:P1} verändert", camera.Name, detector.LastChange);
                    yield return active;
                }
            }
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { /* bereits beendet */ }
            await process.WaitForExitAsync(CancellationToken.None);
            await stderr;
        }

        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException($"Bilderkennung beendet: {lastError ?? $"ffmpeg-Exitcode {process.ExitCode}"}");
    }

    /// <summary>Liest genau ein Bild; false am Ende des Streams.</summary>
    private static async Task<bool> ReadFrameAsync(Stream stream, byte[] frame)
    {
        try
        {
            await stream.ReadExactlyAsync(frame);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    /// <summary>
    /// Nur ein Dekoder-Thread, kein Ton; Bildrate reduzieren, verkleinern (Mittelwert, glättet Rauschen)
    /// und als rohe Graustufen ausgeben: ein Byte je Pixel.
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(IReadOnlyList<string> inputArguments) =>
    [
        "-hide_banner", "-loglevel", "error", "-nostdin",
        "-threads", "1",
        .. inputArguments,
        "-an", "-sn",
        "-vf", $"fps={FramesPerSecond},scale={Width}:{Height}:flags=area,format=gray",
        "-f", "rawvideo", "-pix_fmt", "gray",
        "pipe:1",
    ];
}

/// <summary>Wählt je Kamera die Quelle: Bilderkennung oder ONVIF-Ereignisse der Kamera.</summary>
public sealed class CameraMotionSource(VideoMotionSource video, OnvifMotionSource onvif) : IMotionSource
{
    public IAsyncEnumerable<bool> WatchAsync(Camera camera, CancellationToken ct) =>
        camera.MotionDetection == MotionDetection.Onvif ? onvif.WatchAsync(camera, ct) : video.WatchAsync(camera, ct);
}
