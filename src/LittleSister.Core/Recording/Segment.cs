namespace LittleSister.Core.Recording;

/// <summary>Ein abgeschlossenes Aufnahmestück (fMP4) einer Kamera.</summary>
public sealed class Segment
{
    public long Id { get; set; }

    public Guid CameraId { get; set; }

    public DateTimeOffset StartUtc { get; set; }

    public DateTimeOffset EndUtc { get; set; }

    /// <summary>Aufnahmeordner zum Zeitpunkt der Aufnahme. Wird der Ordner später geändert,
    /// bleiben ältere Segmente im alten Ordner und werden dort weiter verwaltet.</summary>
    public string StorageRoot { get; set; } = "";

    /// <summary>Pfad relativ zum Aufnahmeordner, z. B. "&lt;kamera-id&gt;/20261021/120420.mp4".</summary>
    public string RelativePath { get; set; } = "";

    public long SizeBytes { get; set; }

    /// <summary>Geschützte Segmente (z. B. wichtige Ereignisse) löscht der Ringspeicher nie.</summary>
    public bool IsProtected { get; set; }

    public string FullPath => Path.Combine(StorageRoot, RelativePath);
}
