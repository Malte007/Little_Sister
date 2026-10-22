namespace LittleSister.Infrastructure.Events;

/// <summary>Bewegungszustand einer Kamera: läuft gerade eine Bewegung, wann begann bzw. endete die letzte?</summary>
public sealed record MotionState(bool Active, DateTimeOffset? StartUtc, DateTimeOffset? EndUtc)
{
    public static readonly MotionState None = new(false, null, null);

    /// <summary>Läuft die Bewegung zum Zeitpunkt noch, den Nachlauf eingerechnet?</summary>
    public bool Covers(DateTimeOffset utc, TimeSpan postRoll) => Active || EndUtc is { } end && utc < end + postRoll;
}

/// <summary>
/// Aktueller Bewegungszustand je Kamera im Speicher. Der Ereignisdienst setzt ihn,
/// der Recorder wartet im Modus "Bei Bewegung" darauf.
/// </summary>
public sealed class MotionSignals
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, Entry> entries = [];

    private sealed class Entry
    {
        public MotionState State = MotionState.None;
        public TaskCompletionSource Changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Zustand und eine Aufgabe, die bei der nächsten Änderung fertig wird (beides zusammen, damit keine Änderung verloren geht).</summary>
    public (MotionState State, Task Changed) Snapshot(Guid cameraId)
    {
        lock (gate)
        {
            var entry = EntryOf(cameraId);
            return (entry.State, entry.Changed.Task);
        }
    }

    /// <summary>Meldet Beginn (true) oder Ende (false) einer Bewegung; Wiederholungen ändern nichts.</summary>
    public void Set(Guid cameraId, bool active, DateTimeOffset nowUtc)
    {
        TaskCompletionSource changed;
        lock (gate)
        {
            var entry = EntryOf(cameraId);
            if (entry.State.Active == active)
                return;
            entry.State = active ? new MotionState(true, nowUtc, null) : entry.State with { Active = false, EndUtc = nowUtc };
            changed = entry.Changed;
            entry.Changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        changed.TrySetResult();
    }

    private Entry EntryOf(Guid cameraId)
    {
        if (!entries.TryGetValue(cameraId, out var entry))
            entries[cameraId] = entry = new Entry();
        return entry;
    }
}
