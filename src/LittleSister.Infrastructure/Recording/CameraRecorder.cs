using System.Diagnostics;
using System.Globalization;
using LittleSister.Core.Cameras;
using LittleSister.Core.Recording;
using LittleSister.Infrastructure.Events;
using Microsoft.Extensions.Logging;

namespace LittleSister.Infrastructure.Recording;

/// <summary>
/// Nimmt eine Kamera auf: ein ffmpeg-Prozess kopiert den Hauptstream ohne Umwandlung in Segmente.
/// Bricht ffmpeg ab (Kamera weg, Netzwerk), startet der Recorder nach kurzer Pause neu.
/// Im Modus "Bei Bewegung" läuft ffmpeg nur während einer Bewegung samt Nachlauf;
/// mit Vorlauf läuft der Stream dauernd in einen kurzen Zwischenspeicher, der ständig gelöscht wird.
/// </summary>
public sealed class CameraRecorder(
    Camera camera,
    string recordingsRoot,
    RecordingOptions options,
    ICameraInputFactory input,
    SegmentIndexer indexer,
    CameraStatusRegistry statuses,
    MotionSignals motion,
    TimeProvider time,
    ILogger logger)
{
    // Wartezeiten vor einem Neustart; Ziel laut Konzept: unter 10 s nach Wiedererreichbarkeit
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8)];

    private TimeSpan PreRoll => TimeSpan.FromSeconds(camera.PreRollSeconds);
    private TimeSpan PostRoll => TimeSpan.FromSeconds(camera.PostRollSeconds);

    /// <summary>Segmentlänge im Zwischenspeicher: etwa so lang wie der Vorlauf, damit wenig mehr als nötig übrig bleibt.</summary>
    internal static int BufferSegmentSeconds(int preRollSeconds) => Math.Clamp(preRollSeconds, 2, 10);

    public async Task RunAsync(CancellationToken ct)
    {
        var incoming = SegmentFiles.IncomingDirectory(recordingsRoot, camera.Id);
        Directory.CreateDirectory(incoming);
        await indexer.RecoverIncomingAsync(recordingsRoot, camera.Id, ct);

        if (camera.RecordingMode != RecordingMode.Motion)
            await RunContinuouslyAsync(incoming, options.SegmentSeconds, CommitAsync, ct);
        else if (camera.PreRollSeconds == 0)
            await RunOnMotionAsync(incoming, ct);
        else
            await RunWithPreRollAsync(ct);

        statuses.Update(camera.Id, s => s with { State = CameraState.Idle });
    }

    private Task<Segment?> CommitAsync(string file, TimeSpan duration) =>
        indexer.CommitAsync(recordingsRoot, camera.Id, file, duration, CancellationToken.None);

    /// <summary>ffmpeg dauerhaft laufen lassen und nach Abbrüchen neu starten.</summary>
    private async Task RunContinuouslyAsync(
        string directory, int segmentSeconds, Func<string, TimeSpan, Task<Segment?>> onSegment, CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            statuses.Update(camera.Id, s => s with { State = CameraState.Connecting });
            var result = await RunFfmpegOnceAsync(directory, segmentSeconds, onSegment, ct);
            if (ct.IsCancellationRequested)
                break;

            if (result.SegmentsWritten > 0)
                attempt = 0; // Lief eine Weile stabil: wieder mit kurzer Pause beginnen

            ReportFailure(result);
            await indexer.RecoverIncomingAsync(recordingsRoot, camera.Id, ct);
            if (!await DelayAsync(attempt++, ct))
                break;
        }
    }

    /// <summary>Vorlauf 0: ffmpeg erst bei Bewegung starten und nach dem Nachlauf wieder beenden.</summary>
    private async Task RunOnMotionAsync(string incoming, CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            statuses.Update(camera.Id, s => s with { State = CameraState.Standby, LastError = null });
            if (!await WaitForMotionAsync(ct))
                break;

            using var run = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var stopper = StopAfterMotionAsync(run);
            statuses.Update(camera.Id, s => s with { State = CameraState.Connecting });
            var result = await RunFfmpegOnceAsync(incoming, options.SegmentSeconds, CommitAsync, run.Token);
            var motionOver = await StopAndWaitAsync(run, stopper);
            await indexer.RecoverIncomingAsync(recordingsRoot, camera.Id, ct);
            if (ct.IsCancellationRequested)
                break;
            if (motionOver)
            {
                attempt = 0;
                continue;
            }

            // ffmpeg hat während der Bewegung aufgegeben: Fehler melden und neu versuchen
            if (result.SegmentsWritten > 0)
                attempt = 0;
            ReportFailure(result);
            if (!await DelayAsync(attempt++, ct))
                break;
        }
    }

    /// <summary>Vorlauf > 0: Stream läuft in den Zwischenspeicher, übernommen wird nur das Stück um eine Bewegung.</summary>
    private async Task RunWithPreRollAsync(CancellationToken ct)
    {
        var bufferDirectory = SegmentFiles.BufferDirectory(recordingsRoot, camera.Id);
        // Reste vom letzten Lauf gehören zu keiner Bewegung mehr
        if (Directory.Exists(bufferDirectory))
            Directory.Delete(bufferDirectory, recursive: true);
        Directory.CreateDirectory(bufferDirectory);

        var buffer = new PreRollBuffer(CommitAsync, PreRoll, PostRoll);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var flusher = Task.Run(async () =>
        {
            // Bei Bewegungsbeginn sofort die Segmente des Vorlaufs übernehmen
            while (!stop.IsCancellationRequested)
            {
                var (state, changed) = motion.Snapshot(camera.Id);
                await buffer.FlushAsync(state, time.GetUtcNow());
                await changed.WaitAsync(stop.Token);
            }
        }, CancellationToken.None);

        await RunContinuouslyAsync(bufferDirectory, BufferSegmentSeconds(camera.PreRollSeconds), async (file, duration) =>
        {
            var segment = await buffer.AddAsync(file, duration, motion.Snapshot(camera.Id).State, time.GetUtcNow());
            if (segment is null)
                statuses.Update(camera.Id, s => s with { State = CameraState.Standby, LastError = null });
            return segment;
        }, ct);

        await stop.CancelAsync();
        try
        {
            await flusher;
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Wartet, bis die Kamera Bewegung meldet.</summary>
    /// <returns>false bei Abbruch.</returns>
    private async Task<bool> WaitForMotionAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var (state, changed) = motion.Snapshot(camera.Id);
                if (state.Active)
                    return true;
                await changed.WaitAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Beendet den Lauf, sobald Bewegung und Nachlauf vorbei sind.</summary>
    /// <returns>true, wenn der Lauf deshalb beendet wurde.</returns>
    private async Task<bool> StopAfterMotionAsync(CancellationTokenSource run)
    {
        try
        {
            while (true)
            {
                var (state, changed) = motion.Snapshot(camera.Id);
                var now = time.GetUtcNow();
                if (!state.Covers(now, PostRoll))
                {
                    await run.CancelAsync();
                    return true;
                }
                // Während der Bewegung auf ihr Ende warten, danach bis zum Ende des Nachlaufs
                var wait = state.Active ? Timeout.InfiniteTimeSpan : state.EndUtc!.Value + PostRoll - now;
                await Task.WhenAny(changed, Task.Delay(wait, time, run.Token)).Unwrap();
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task<bool> StopAndWaitAsync(CancellationTokenSource run, Task<bool> stopper)
    {
        if (stopper.IsCompleted)
            return await stopper;
        await run.CancelAsync();
        return await stopper;
    }

    private void ReportFailure(RunResult result)
    {
        var error = result.LastError ?? $"ffmpeg beendet (Code {result.ExitCode})";
        statuses.Update(camera.Id, s => s with { State = CameraState.Error, LastError = error, Restarts = s.Restarts + 1 });
        logger.LogWarning("Aufnahme {Camera} unterbrochen: {Error}", camera.Name, error);
    }

    /// <returns>false bei Abbruch.</returns>
    private async Task<bool> DelayAsync(int attempt, CancellationToken ct)
    {
        try
        {
            await Task.Delay(Backoff[Math.Min(attempt, Backoff.Length - 1)], time, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private sealed record RunResult(int ExitCode, string? LastError, int SegmentsWritten);

    private async Task<RunResult> RunFfmpegOnceAsync(
        string directory, int segmentSeconds, Func<string, TimeSpan, Task<Segment?>> onSegment, CancellationToken ct)
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
        foreach (var arg in BuildArguments(input.CreateInputArguments(camera), directory, segmentSeconds, camera.Rotation))
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
                var file = Path.Combine(directory, Path.GetFileName(parts[0]));
                segments++;
                var segment = await onSegment(file, TimeSpan.FromSeconds(to - from));
                if (segment is null)
                    continue;
                // Erst mit dem ersten übernommenen Segment gilt die Kamera als aufnehmend
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

    /// <summary>ffmpeg-Aufruf: Video kopieren (keine CPU-Last), Ton nach AAC, damit auch G.711-Kameras in MP4 passen.
    /// Eine Drehung wird nur als Markierung in die Datei geschrieben; Player drehen beim Abspielen.</summary>
    internal static IReadOnlyList<string> BuildArguments(IReadOnlyList<string> inputArguments, string incomingDirectory, int segmentSeconds, int rotation = 0) =>
    [
        "-hide_banner", "-loglevel", "warning",
        // ffmpeg zählt gegen den Uhrzeigersinn, Little Sister im Uhrzeigersinn
        .. rotation == 0 ? Array.Empty<string>() : ["-display_rotation:v:0", (-rotation).ToString(CultureInfo.InvariantCulture)],
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
