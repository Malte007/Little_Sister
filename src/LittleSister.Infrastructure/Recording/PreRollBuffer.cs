using LittleSister.Core.Recording;
using LittleSister.Infrastructure.Events;

namespace LittleSister.Infrastructure.Recording;

/// <summary>
/// Zwischenspeicher für den Vorlauf: Neue Segmente bleiben im Pufferordner, bis klar ist, ob eine Bewegung sie braucht.
/// Segmente um eine Bewegung herum werden übernommen, alle anderen gelöscht, sobald sie älter als der Vorlauf sind.
/// </summary>
internal sealed class PreRollBuffer(Func<string, TimeSpan, Task<Segment?>> commit, TimeSpan preRoll, TimeSpan postRoll)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly List<(string File, DateTimeOffset Start, DateTimeOffset End, TimeSpan Duration)> pending = [];

    /// <summary>Nimmt ein fertiges Segment auf.</summary>
    /// <returns>Das übernommene Segment, oder null, wenn es (vorerst) nur im Puffer liegt.</returns>
    public async Task<Segment?> AddAsync(string file, TimeSpan duration, MotionState motion, DateTimeOffset nowUtc)
    {
        if (!SegmentFiles.TryParseStart(file, out var start))
        {
            TryDelete(file);
            return null;
        }

        await gate.WaitAsync();
        try
        {
            Segment? result = null;
            if (Keep(motion, start, start + duration, preRoll, postRoll))
                result = await commit(file, duration);
            else
                pending.Add((file, start, start + duration, duration));
            Prune(nowUtc);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Bewegung hat sich geändert: wartende Segmente im Vorlauf übernehmen, alte löschen.</summary>
    public async Task FlushAsync(MotionState motion, DateTimeOffset nowUtc)
    {
        await gate.WaitAsync();
        try
        {
            foreach (var item in pending.Where(p => Keep(motion, p.Start, p.End, preRoll, postRoll)).ToList())
            {
                pending.Remove(item);
                await commit(item.File, item.Duration);
            }
            Prune(nowUtc);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Berührt das Segment die Bewegung samt Vor- und Nachlauf?</summary>
    internal static bool Keep(MotionState motion, DateTimeOffset start, DateTimeOffset end, TimeSpan preRoll, TimeSpan postRoll) =>
        motion.StartUtc is { } from
        && end > from - preRoll
        && (motion.Active || start < motion.EndUtc!.Value + postRoll);

    private void Prune(DateTimeOffset nowUtc)
    {
        foreach (var item in pending.Where(p => p.End < nowUtc - preRoll).ToList())
        {
            pending.Remove(item);
            TryDelete(item.File);
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* nächster Start räumt auf */ }
    }
}
