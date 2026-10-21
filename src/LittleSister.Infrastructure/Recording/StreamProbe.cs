using System.Diagnostics;
using System.Text.Json;
using LittleSister.Core.Cameras;
using Microsoft.Extensions.Options;

namespace LittleSister.Infrastructure.Recording;

/// <summary>Ergebnis einer Stream-Prüfung: Codec und Auflösung des Videos.</summary>
public sealed record StreamProbeResult(bool Success, string? Codec, int Width, int Height, string? Error);

/// <summary>Prüft per ffprobe, ob ein RTSP-Stream erreichbar ist, bevor eine Kamera gespeichert wird.</summary>
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

    /// <summary>Übersetzt typische ffprobe-Meldungen in Fehlercodes für die Oberfläche.</summary>
    internal static string ClassifyError(string stderr) => stderr switch
    {
        _ when stderr.Contains("401") || stderr.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) => "streamUnauthorized",
        _ when stderr.Contains("404") || stderr.Contains("Not Found", StringComparison.OrdinalIgnoreCase) => "streamNotFound",
        _ when stderr.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) => "streamRefused",
        _ => "streamUnreachable",
    };
}
