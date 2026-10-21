namespace LittleSister.Core.Events;

/// <summary>Ein Ereignis einer Kamera, z. B. Bewegung; erscheint als Marker auf der Zeitleiste.</summary>
public sealed class CameraEvent
{
    public const string Motion = "motion";

    public long Id { get; set; }

    public Guid CameraId { get; set; }

    /// <summary>Art des Ereignisses, z. B. <see cref="Motion"/>.</summary>
    public string Kind { get; set; } = Motion;

    public DateTimeOffset StartUtc { get; set; }

    /// <summary>Ende; null, solange das Ereignis noch andauert.</summary>
    public DateTimeOffset? EndUtc { get; set; }
}
