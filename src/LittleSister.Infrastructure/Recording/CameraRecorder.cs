using System.Diagnostics;
using System.Globalization;
using LittleSister.Core.Cameras;
using Microsoft.Extensions.Logging;

namespace LittleSister.Infrastructure.Recording;

/// <summary>
/// Nimmt eine Kamera auf: ein ffmpeg-Prozess kopiert den Hauptstream ohne Umwandlung in Segmente.
/// Bricht ffmpeg ab (Kamera weg, Netzwerk), startet der Recorder nach kurzer Pause neu.
/// </summary>
public sealed class CameraRecorder(
    Camera camera,
    string recordingsRoot,
    RecordingOptions options,
    ICameraInputFactory input,
    SegmentIndexer indexer,
    CameraStatusRegistry statuses,
    TimeProvider time,
    ILogger logger)
{
    // Wartezeiten vor einem Neustart; Ziel laut Konzept: unter 10 s nach Wiedererreichbarkeit
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8)];

    public async Task RunAsync(CancellationToken ct)
    {
        var incoming = SegmentFiles.IncomingDirectory(recordingsRoot, camera.Id);
        Directory.CreateDirectory(incoming);
        await indexer.RecoverIncomingAsync(recordingsRoot, camera.Id, ct);

        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            statuses.Update(camera.Id, s => s with { State = CameraState.Connecting });
            var result = await RunFfmpegOnceAsync(incoming, ct);
            if (ct.IsCancellationRequested)
                break;

            if (result.SegmentsWritten > 0)
                attempt = 0; // Lief eine Weile stabil: wieder mit kurzer Pause beginnen

            var error = result.LastError ?? $"ffmpeg beendet (Code {result.ExitCode})";
            statuses.Update(camera.Id, s => s with { State = CameraState.Error, LastError = error, Restarts = s.Restarts + 1 });
            logger.LogWarning("Aufnahme {Camera} unterbrochen: {Error}", camera.Name, error);

            await indexer.RecoverIncomingAsync(recordingsRoot, camera.Id, ct);
            try
            {
                await Task.Delay(Backoff[Math.Min(attempt++, Backoff.Length - 1)], time, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        statuses.Update(camera.Id, s => s with { State = CameraState.Idle });
    }

    private sealed record RunResult(int ExitCode, string? LastError, int SegmentsWritten);

    private async Task<RunResult> RunFfmpegOnceAsync(string incoming, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(options.FfmpegPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // Dateinamen per strftime immer in UTC, unabhängig von der Zeitzone des Servers
        psi.Environment["TZ"] = "UTC";
        foreach (var arg in BuildArguments(input.CreateInputArguments(camera), incoming, options.SegmentSeconds))
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new RunResult(-1, $"ffmpeg nicht gefunden ({options.FfmpegPath}): {ex.Message}", 0);
        }

        var lastErrors = new Queue<string>();
        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(CancellationToken.None) is { } line)
            {
                logger.LogDebug("ffmpeg {Camera}: {Line}", camera.Name, line);
                lock (lastErrors)
                {
                    lastErrors.Enqueue(line);
                    if (lastErrors.Count > 3)
                        lastErrors.Dequeue();
                }
            }
        }, CancellationToken.None);

        // Bei Abbruch ffmpeg sauber mit "q" beenden, damit das letzte Segment vollständig geschrieben wird
        await using var stop = ct.Register(() =>
        {
            try
            {
                process.StandardInput.Write('q');
                process.StandardInput.Flush();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { /* Prozess bereits beendet */ }

            // Reagiert ffmpeg nicht (z. B. hängt beim Verbindungsaufbau), nach 10 s hart beenden
            _ = Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None).ContinueWith(_ =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { /* bereits beendet und freigegeben */ }
            }, TaskScheduler.Default);
        });

        var segments = 0;
        while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
        {
            // Segmentliste im CSV-Format: Dateiname,Start,Ende (Sekunden seit Streambeginn)
            var parts = line.Split(',');
            if (parts.Length < 3
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var from)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var to))
                continue;

            try
            {
                var file = Path.Combine(incoming, Path.GetFileName(parts[0]));
                var segment = await indexer.CommitAsync(recordingsRoot, camera.Id, file, TimeSpan.FromSeconds(to - from), CancellationToken.None);
                if (segment is null)
                    continue;
                // Erst mit dem ersten fertigen Segment gilt die Kamera als aufnehmend
                segments++;
                statuses.Update(camera.Id, s => s with { State = CameraState.Recording, LastError = null, LastSegmentUtc = segment.EndUtc });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                logger.LogError(ex, "Segment von {Camera} konnte nicht übernommen werden", camera.Name);
            }
        }

        await process.WaitForExitAsync(CancellationToken.None);
        await stderrTask;

        string? lastError;
        lock (lastErrors)
            lastError = lastErrors.LastOrDefault();
        return new RunResult(process.ExitCode, lastError, segments);
    }

    /// <summary>ffmpeg-Aufruf: Video kopieren (keine CPU-Last), Ton nach AAC, damit auch G.711-Kameras in MP4 passen.</summary>
    internal static IReadOnlyList<string> BuildArguments(IReadOnlyList<string> inputArguments, string incomingDirectory, int segmentSeconds) =>
    [
        "-hide_banner", "-loglevel", "warning",
        .. inputArguments,
        "-map", "0:v:0", "-map", "0:a:0?",
        "-c:v", "copy", "-c:a", "aac", "-b:a", "64k",
        "-f", "segment",
        "-segment_time", segmentSeconds.ToString(CultureInfo.InvariantCulture),
        "-segment_format", "mp4",
        // Fragmentiertes MP4: auch bei Absturz lesbar und später per HLS abspielbar
        "-segment_format_options", "movflags=+frag_keyframe+empty_moov+default_base_moof",
        "-reset_timestamps", "1",
        "-strftime", "1",
        "-segment_list", "pipe:1", "-segment_list_type", "csv",
        Path.Combine(incomingDirectory, SegmentFiles.IncomingPattern),
    ];
}
