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

    public DatabaseOptions Database { get; set; } = new();

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
