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

    public LiveOptions Live { get; set; } = new();

    public EventOptions Events { get; set; } = new();

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

/// <summary>Live-Ansicht über go2rtc, das Little Sister selbst startet.</summary>
public sealed class LiveOptions
{
    /// <summary>Pfad zur go2rtc-Programmdatei; fehlt sie, ist die Live-Ansicht abgeschaltet.</summary>
    public string Go2rtcPath { get; set; } = "go2rtc";

    /// <summary>Port der go2rtc-API; nur auf 127.0.0.1 erreichbar, der Browser geht immer über Little Sister.</summary>
    public int ApiPort { get; set; } = 1984;

    /// <summary>Port für WebRTC (TCP und UDP); muss vom Browser aus erreichbar sein.</summary>
    public int WebRtcPort { get; set; } = 8555;

    /// <summary>Zusätzliche WebRTC-Adressen, z. B. "192.168.1.10:8555" hinter NAT oder Docker-Bridge.</summary>
    public List<string> WebRtcCandidates { get; set; } = [];
}

/// <summary>Kamera-Ereignisse (Bewegung).</summary>
public sealed class EventOptions
{
    /// <summary>
    /// Adresse, unter der Kameras Little Sister erreichen, z. B. "http://192.168.1.10:8080".
    /// Nur für Kameras ohne PullPoint nötig, die ihre Meldungen selbst schicken; leer = automatisch ermitteln.
    /// </summary>
    public string? CallbackUrl { get; set; }
}
