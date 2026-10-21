using LittleSister.Core.Settings;

namespace LittleSister.Core.Setup;

/// <summary>Prüft die Eingaben der Ersteinrichtung, bevor etwas gespeichert wird.</summary>
public static class SetupValidator
{
    /// <summary>Liefert Fehler je Feld; ein leeres Ergebnis bedeutet gültig.</summary>
    public static Dictionary<string, string[]> Validate(SetupRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.UserName))
            errors[nameof(request.UserName)] = ["required"];

        if (string.IsNullOrEmpty(request.Password))
            errors[nameof(request.Password)] = ["required"];

        if (!SupportedLanguages.IsSupported(request.Language))
            errors[nameof(request.Language)] = ["unsupported"];

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZoneId ?? "", out _))
            errors[nameof(request.TimeZoneId)] = ["unknown"];

        // Relative Pfade wären vom Arbeitsverzeichnis abhängig und damit fehleranfällig
        if (string.IsNullOrWhiteSpace(request.RecordingsPath) || !Path.IsPathFullyQualified(request.RecordingsPath))
            errors[nameof(request.RecordingsPath)] = ["absolutePathRequired"];

        return errors;
    }
}
