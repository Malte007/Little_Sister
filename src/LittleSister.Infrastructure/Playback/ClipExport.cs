using System.Diagnostics;
using System.Globalization;
using System.Text;
using LittleSister.Core.Recording;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LittleSister.Infrastructure.Playback;

/// <summary>
/// Setzt einen Zeitraum aus mehreren Segmenten zu einer MP4-Datei zusammen, ohne neu zu kodieren.
/// Der Anfang springt dabei auf das vorherige Schlüsselbild, kann also etwas früher liegen.
/// </summary>
public sealed class ClipExport(IOptions<LittleSisterOptions> options, ILogger<ClipExport> logger)
{
    /// <summary>Längster Zeitraum, der in einem Stück exportiert wird.</summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromHours(24);

    /// <summary>Schreibt den Zeitraum als fragmentiertes MP4 in <paramref name="output"/>.</summary>
    /// <returns>false, wenn ffmpeg nicht gestartet werden konnte oder mit Fehler endete.</returns>
    public async Task<bool> WriteAsync(IReadOnlyList<Segment> segments, DateTimeOffset fromUtc, DateTimeOffset toUtc, Stream output, CancellationToken ct)
    {
        var list = Path.Combine(Path.GetTempPath(), $"littlesister-export-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(list, BuildConcatList(segments, fromUtc, toUtc), ct);
        try
        {
            var psi = new ProcessStartInfo(options.Value.Recording.FfmpegPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in BuildArguments(list))
                psi.ArgumentList.Add(arg);

            using var process = new Process { StartInfo = psi };
            try
            {
                process.Start();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                logger.LogError(ex, "ffmpeg für den Export nicht gefunden");
                return false;
            }

            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.StandardOutput.BaseStream.CopyToAsync(output, ct);
                await process.WaitForExitAsync(ct);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                // Download abgebrochen: ffmpeg nicht weiterlaufen lassen
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                throw;
            }

            if (process.ExitCode != 0)
                logger.LogWarning("Export fehlgeschlagen: {Error}", (await stderr).Trim());
            return process.ExitCode == 0;
        }
        finally
        {
            File.Delete(list);
        }
    }

    /// <summary>Liste für ffmpegs concat-Demuxer; Anfang und Ende werden im ersten bzw. letzten Segment beschnitten.</summary>
    internal static string BuildConcatList(IReadOnlyList<Segment> segments, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        var sb = new StringBuilder("ffconcat version 1.0\n");
        foreach (var s in segments)
        {
            // Einfache Anführungszeichen im Pfad werden als '\'' geschrieben
            sb.Append("file '").Append(s.FullPath.Replace("'", @"'\''")).Append("'\n");
            if (fromUtc > s.StartUtc)
                sb.Append(CultureInfo.InvariantCulture, $"inpoint {(fromUtc - s.StartUtc).TotalSeconds:0.###}\n");
            if (toUtc < s.EndUtc)
                sb.Append(CultureInfo.InvariantCulture, $"outpoint {(toUtc - s.StartUtc).TotalSeconds:0.###}\n");
        }
        return sb.ToString();
    }

    internal static IReadOnlyList<string> BuildArguments(string concatList) =>
    [
        "-hide_banner", "-loglevel", "error",
        "-f", "concat", "-safe", "0", "-i", concatList,
        "-map", "0:v:0", "-map", "0:a:0?",
        "-c", "copy",
        // Fragmentiert, damit die Datei direkt an den Browser fließen kann, ohne Zwischendatei
        "-movflags", "frag_keyframe+empty_moov+default_base_moof",
        "-f", "mp4", "pipe:1",
    ];
}
