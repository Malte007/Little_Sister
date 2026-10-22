using System.Diagnostics;
using System.Text.Json;
using LittleSister.Core.Cameras;
using Microsoft.Extensions.Options;

namespace LittleSister.Infrastructure.Recording;

/// <summary>Ergebnis einer Stream-Prüfung: Codec und Auflösung des Videos.</summary>
public sealed record StreamProbeResult(bool Success, string? Codec, int Width, int Height, string? Error);

/// <summary>Prüft per ffprobe, ob ein RTSP-Stream erreichbar ist, bevor eine Kamera gespeichert wird; liefert auf Wunsch ein Standbild.</summary>
public sealed class StreamProbe(IOptions<LittleSisterOptions> options)
{
    public async Task<StreamProbeResult> ProbeAsync(string url, string? username, string? password, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(options.Value.Recording.FfprobePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[]
        {
            "-v", "error", "-rtsp_transport", "tcp", "-timeout", "8000000",
            "-select_streams", "v:0", "-show_entries", "stream=codec_name,width,height", "-of", "json",
            StreamUrl.WithCredentials(url, username, password),
        })
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new StreamProbeResult(false, null, 0, 0, "ffprobeMissing");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return new StreamProbeResult(false, null, 0, 0, "streamTimeout");
        }

        if (process.ExitCode != 0)
            return new StreamProbeResult(false, null, 0, 0, ClassifyError(await stderr));

        using var json = JsonDocument.Parse(await stdout);
        var stream = json.RootElement.TryGetProperty("streams", out var streams) && streams.GetArrayLength() > 0 ? streams[0] : default;
        if (stream.ValueKind != JsonValueKind.Object)
            return new StreamProbeResult(false, null, 0, 0, "noVideo");

        return new StreamProbeResult(true,
            stream.TryGetProperty("codec_name", out var c) ? c.GetString() : null,
            stream.TryGetProperty("width", out var w) ? w.GetInt32() : 0,
            stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0,
            null);
    }

    /// <summary>Breite des Vorschaubilds; die Höhe folgt dem Seitenverhältnis.</summary>
    public const int SnapshotWidth = 640;

    /// <summary>
    /// Holt ein einzelnes Standbild direkt vom Stream, z. B. für die Erkennungsbereiche einer noch nicht gespeicherten Kamera.
    /// </summary>
    /// <returns>JPEG-Bytes oder einen Fehlercode wie bei <see cref="ProbeAsync"/>.</returns>
    public async Task<(byte[]? Jpeg, string? Error)> SnapshotAsync(string url, string? username, string? password, CancellationToken ct) =>
        await SnapshotAsync(["-rtsp_transport", "tcp", "-timeout", "8000000", "-i", StreamUrl.WithCredentials(url, username, password)], ct);

    internal async Task<(byte[]? Jpeg, string? Error)> SnapshotAsync(IReadOnlyList<string> inputArguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(options.Value.Recording.FfmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in BuildSnapshotArguments(inputArguments))
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (null, "ffmpegMissing");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var image = new MemoryStream();
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(image, timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await stdout;
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            ct.ThrowIfCancellationRequested();
            return (null, "streamTimeout");
        }

        if (process.ExitCode != 0)
            return (null, ClassifyError(await stderr));
        return image.Length > 0 ? (image.ToArray(), null) : (null, "noVideo");
    }

    /// <summary>Ein Bild, verkleinert, als JPEG nach stdout.</summary>
    internal static IReadOnlyList<string> BuildSnapshotArguments(IReadOnlyList<string> inputArguments) =>
    [
        "-hide_banner", "-loglevel", "error", "-nostdin",
        .. inputArguments,
        "-an", "-frames:v", "1",
        "-vf", $"scale={SnapshotWidth}:-2",
        "-f", "image2", "-c:v", "mjpeg", "-q:v", "5",
        "pipe:1",
    ];

    /// <summary>Übersetzt typische ffprobe-Meldungen in Fehlercodes für die Oberfläche.</summary>
    internal static string ClassifyError(string stderr) => stderr switch
    {
        _ when stderr.Contains("401") || stderr.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) => "streamUnauthorized",
        _ when stderr.Contains("404") || stderr.Contains("Not Found", StringComparison.OrdinalIgnoreCase) => "streamNotFound",
        _ when stderr.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) => "streamRefused",
        _ => "streamUnreachable",
    };
}
