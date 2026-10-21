using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using LittleSister.Core.Recording;
using LittleSister.Core.Settings;
using LittleSister.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LittleSister.Infrastructure.Playback;

/// <summary>
/// Vorschaubilder für die Zeitleiste: pro Segment ein Bildstreifen mit einem Bild alle
/// <see cref="IntervalSeconds"/> Sekunden, abgelegt neben dem Segment (120420.mp4 → 120420.jpg).
/// ffmpeg dekodiert dafür nur Schlüsselbilder; das kostet kaum Rechenzeit.
/// Neue Segmente werden im Hintergrund nacheinander bearbeitet, ältere bei der ersten Anfrage.
/// Hat der Admin die Vorschaubilder ausgeschaltet, entstehen keine neuen.
/// </summary>
public sealed class SegmentThumbnails(
    IServiceScopeFactory scopes, IOptions<LittleSisterOptions> options, ILogger<SegmentThumbnails> logger) : BackgroundService
{
    /// <summary>Abstand der Bilder im Streifen.</summary>
    public const int IntervalSeconds = 10;

    /// <summary>Höhe eines Bildes in Pixeln; die Breite folgt dem Seitenverhältnis (auch bei gedrehten Kameras).</summary>
    public const int Height = 120;

    // Läuft die Aufnahme schneller als die Bilder entstehen, gehen die ältesten Aufträge verloren;
    // sie werden dann bei Bedarf nachgeholt
    private readonly Channel<Segment> queue = Channel.CreateBounded<Segment>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    // Höchstens zwei ffmpeg gleichzeitig, auch wenn jemand schnell über ältere Aufnahmen fährt
    private readonly SemaphoreSlim slots = new(2);

    // Laufende Aufträge je Datei, damit gleichzeitige Anfragen ffmpeg nicht mehrfach starten
    private readonly ConcurrentDictionary<string, Lazy<Task<bool>>> running = new();

    /// <summary>Ablageort des Bildstreifens zu einem Segment.</summary>
    public static string PathOf(Segment segment) => Path.ChangeExtension(segment.FullPath, ".jpg");

    /// <summary>Anzahl der Bilder im Streifen; das letzte deckt den Rest des Segments ab.</summary>
    public static int CountOf(Segment segment) =>
        Math.Max(1, (int)Math.Ceiling((segment.EndUtc - segment.StartUtc).TotalSeconds / IntervalSeconds - 0.001));

    /// <summary>Merkt ein neues Segment für die Hintergrundbearbeitung vor.</summary>
    public void Enqueue(Segment segment) => queue.Writer.TryWrite(segment);

    /// <summary>Erzeugt den Bildstreifen, falls er noch fehlt.</summary>
    /// <returns>true, wenn der Streifen danach vorhanden ist.</returns>
    public Task<bool> EnsureAsync(Segment segment)
    {
        var target = PathOf(segment);
        if (File.Exists(target))
            return Task.FromResult(true);

        var job = running.GetOrAdd(target, _ => new Lazy<Task<bool>>(() => CreateAsync(segment, target)));
        return job.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var segment in queue.Reader.ReadAllAsync(stoppingToken))
            {
                if (await IsEnabledAsync(stoppingToken))
                    await EnsureAsync(segment);
            }
        }
        catch (OperationCanceledException) { /* Dienst wird beendet */ }
    }

    /// <summary>Ob Vorschaubilder erzeugt werden sollen (Einstellung des Admins, Standard: ja).</summary>
    public async Task<bool> IsEnabledAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();
        return await db.SystemSettings.AsNoTracking().Where(s => s.Id == SystemSettings.SingletonId)
            .Select(s => (bool?)s.ThumbnailsEnabled).FirstOrDefaultAsync(ct) ?? true;
    }

    private async Task<bool> CreateAsync(Segment segment, string target)
    {
        var temp = target + ".tmp";
        await slots.WaitAsync();
        try
        {
            if (!File.Exists(segment.FullPath))
                return false;

            var psi = new ProcessStartInfo(options.Value.Recording.FfmpegPath)
            {
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in BuildArguments(segment.FullPath, temp, CountOf(segment)))
                psi.ArgumentList.Add(arg);

            using var process = new Process { StartInfo = psi };
            process.Start();
            var stderr = process.StandardError.ReadToEndAsync();

            // Hängt ffmpeg (z. B. defekte Datei), nach 30 s abbrechen
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                logger.LogWarning("Vorschaubilder für {Path} abgebrochen (Zeitüberschreitung)", segment.RelativePath);
                return false;
            }

            if (process.ExitCode != 0 || !File.Exists(temp))
            {
                logger.LogWarning("Vorschaubilder für {Path} fehlgeschlagen: {Error}", segment.RelativePath, (await stderr).Trim());
                return false;
            }

            File.Move(temp, target, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "Vorschaubilder für {Path} fehlgeschlagen", segment.RelativePath);
            return false;
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
            slots.Release();
            running.TryRemove(target, out _);
        }
    }

    /// <summary>
    /// Nur Schlüsselbilder dekodieren, je Intervall das nächstgelegene nehmen, verkleinern und
    /// nebeneinander zu einem Bild zusammensetzen. Fehlt am Ende ein Bild, bleibt die Kachel schwarz.
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(string input, string output, int count) =>
    [
        "-hide_banner", "-loglevel", "error", "-y",
        "-skip_frame", "nokey",
        "-i", input,
        "-an", "-sn", "-dn",
        "-vf", string.Create(CultureInfo.InvariantCulture,
            $"setpts=PTS-STARTPTS,fps=1/{IntervalSeconds}:start_time=0:round=up:eof_action=pass,scale=-2:{Height},tile={count}x1"),
        "-frames:v", "1",
        "-q:v", "6",
        "-f", "image2", "-c:v", "mjpeg",
        output,
    ];
}
