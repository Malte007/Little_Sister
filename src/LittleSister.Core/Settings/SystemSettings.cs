namespace LittleSister.Core.Settings;

/// <summary>Globale Einstellungen der Installation; es gibt genau einen Datensatz.</summary>
public sealed class SystemSettings
{
    /// <summary>Fester Schlüssel des einzigen Datensatzes.</summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Sprache der Oberfläche, z. B. "de" oder "en".</summary>
    public string Language { get; set; } = SupportedLanguages.Default;

    /// <summary>IANA-Zeitzone für die Anzeige, z. B. "Europe/Berlin". Gespeichert wird immer in UTC.</summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>Absoluter Pfad, unter dem die Aufnahmen abgelegt werden.</summary>
    public string RecordingsPath { get; set; } = "";

    /// <summary>Globale Freispeichergrenze in GB: wird sie unterschritten, löscht der Ringspeicher die ältesten Aufnahmen.</summary>
    public double MinFreeGb { get; set; } = 10;

    /// <summary>Vorschaubilder für die Zeitleiste erzeugen; ausgeschaltet spart das etwas Rechenzeit.</summary>
    public bool ThumbnailsEnabled { get; set; } = true;

    /// <summary>Zeitpunkt, zu dem die Ersteinrichtung abgeschlossen wurde.</summary>
    public DateTimeOffset SetupCompletedAtUtc { get; set; }
}
