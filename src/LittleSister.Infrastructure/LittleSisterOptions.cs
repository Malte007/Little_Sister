namespace LittleSister.Infrastructure;

/// <summary>
/// Konfiguration aus appsettings.json bzw. Umgebungsvariablen
/// (z. B. <c>LittleSister__DataPath=/data</c>).
/// </summary>
public sealed class LittleSisterOptions
{
    public const string SectionName = "LittleSister";

    /// <summary>Ordner für Datenbank, Schlüssel und Sicherungen.</summary>
    public string DataPath { get; set; } = "data";

    /// <summary>Vorschlag für den Aufnahmeordner im Einrichtungsassistenten.</summary>
    public string DefaultRecordingsPath { get; set; } = "/recordings";

    /// <summary>Ordner für die Schlüssel (Cookies, Kamera-Passwörter); leer = &lt;DataPath&gt;/keys.
    /// Lässt sich so getrennt von der Datenbank ablegen.</summary>
    public string? KeysPath { get; set; }

    public DatabaseOptions Database { get; set; } = new();

    public RecordingOptions Recording { get; set; } = new();

    public string GetFullKeysPath() => Path.GetFullPath(KeysPath ?? Path.Combine(DataPath, "keys"));

    /// <summary>Liefert den Datenordner als absoluten Pfad.</summary>
    public string GetFullDataPath() => Path.GetFullPath(DataPath);
}

/// <summary>Datenbankanbindung; weitere Provider folgen per Konfiguration.</summary>
public sealed class DatabaseOptions
{
    /// <summary>Aktuell nur "Sqlite"; PostgreSQL/MySQL/MariaDB sind vorgesehen.</summary>
    public string Provider { get; set; } = "Sqlite";

    /// <summary>Leer bedeutet: SQLite-Datei im Datenordner.</summary>
    public string? ConnectionString { get; set; }
}

/// <summary>Einstellungen für Aufnahme und Ringspeicher.</summary>
public sealed class RecordingOptions
{
    /// <summary>Länge eines Aufnahmesegments in Sekunden (Konzept: 1–5 Minuten).</summary>
    public int SegmentSeconds { get; set; } = 60;

    public string FfmpegPath { get; set; } = "ffmpeg";

    public string FfprobePath { get; set; } = "ffprobe";

    /// <summary>Wie oft der Ringspeicher Kontingente und Freispeicher prüft.</summary>
    public int RetentionIntervalSeconds { get; set; } = 60;
}
