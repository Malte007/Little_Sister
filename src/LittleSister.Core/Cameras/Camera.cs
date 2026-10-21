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

    /// <summary>Daueraufnahme an/aus; ereignisgesteuerte Aufnahme folgt später.</summary>
    public bool RecordingEnabled { get; set; } = true;

    /// <summary>Speicherkontingent dieser Kamera in GB.</summary>
    public double QuotaGb { get; set; } = 200;

    /// <summary>Maximale Aufbewahrung in Tagen (Datenschutz); null = nur Kontingent zählt.</summary>
    public int? MaxRetentionDays { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public long QuotaBytes => (long)(QuotaGb * 1024 * 1024 * 1024);
}
