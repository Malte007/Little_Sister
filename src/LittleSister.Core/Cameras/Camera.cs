namespace LittleSister.Core.Cameras;

/// <summary>Eine eingebundene Kamera mit Stream-Adressen und Speicherregeln.</summary>
public sealed class Camera
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>Hauptstream (volle Auflösung) für die Aufnahme, ohne Zugangsdaten in der URL.</summary>
    public string MainStreamUrl { get; set; } = "";

    /// <summary>Substream (geringe Auflösung) für Live-Kacheln und Vorschaubilder; optional.</summary>
    public string? SubStreamUrl { get; set; }

    public string? Username { get; set; }

    /// <summary>Passwort, verschlüsselt per Data Protection; nie im Klartext gespeichert oder ausgeliefert.</summary>
    public string? ProtectedPassword { get; set; }

    /// <summary>ONVIF-Geräteadresse, falls die Kamera per ONVIF eingebunden wurde.</summary>
    public string? OnvifAddress { get; set; }

    public string? Manufacturer { get; set; }

    public string? Model { get; set; }

    /// <summary>Aus, dauernd oder nur bei Bewegung.</summary>
    public RecordingMode RecordingMode { get; set; } = RecordingMode.Continuous;

    /// <summary>Woher die Bewegung kommt: aus dem Bild (Little Sister) oder als ONVIF-Ereignis der Kamera.</summary>
    public MotionDetection MotionDetection { get; set; } = MotionDetection.Video;

    /// <summary>Empfindlichkeit der Bilderkennung, 1 (grob) bis 100 (sehr empfindlich).</summary>
    public int MotionSensitivity { get; set; } = 50;

    /// <summary>Erkennungsbereiche für die Bilderkennung (siehe <see cref="Cameras.MotionZones"/>); null = ganzes Bild.</summary>
    public string? MotionZones { get; set; }

    /// <summary>Bei Bewegung: so viele Sekunden vor dem Ereignis aufheben.</summary>
    public int PreRollSeconds { get; set; } = 10;

    /// <summary>Bei Bewegung: so viele Sekunden nach dem Ende des Ereignisses aufheben.</summary>
    public int PostRollSeconds { get; set; } = 30;

    /// <summary>Läuft für diese Kamera ein Recorder?</summary>
    public bool RecordingEnabled => RecordingMode != RecordingMode.Off;

    /// <summary>Drehung des Bildes im Uhrzeigersinn (0, 90, 180, 270); wird nur angezeigt, nie umgerechnet.</summary>
    public int Rotation { get; set; }

    /// <summary>Speicherkontingent dieser Kamera in GB.</summary>
    public double QuotaGb { get; set; } = 200;

    /// <summary>Maximale Aufbewahrung in Tagen (Datenschutz); null = nur Kontingent zählt.</summary>
    public int? MaxRetentionDays { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public long QuotaBytes => (long)(QuotaGb * 1024 * 1024 * 1024);
}

/// <summary>Aufnahmeart einer Kamera.</summary>
public enum RecordingMode
{
    Off,
    Continuous,
    /// <summary>Aufgenommen wird nur bei Bewegung samt Vor- und Nachlauf; ohne Vorlauf läuft der Stream nur während der Bewegung.</summary>
    Motion,
}

/// <summary>Quelle der Bewegungserkennung.</summary>
public enum MotionDetection
{
    /// <summary>Little Sister vergleicht die Bilder des Substreams; geht mit jeder Kamera.</summary>
    Video,
    /// <summary>Die Kamera meldet Bewegung per ONVIF-Ereignis.</summary>
    Onvif,
}
