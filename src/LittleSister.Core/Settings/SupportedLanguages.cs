namespace LittleSister.Core.Settings;

/// <summary>Sprachen, für die es Sprachdateien in der Oberfläche gibt.</summary>
public static class SupportedLanguages
{
    public const string Default = "de";

    public static IReadOnlyList<string> All { get; } = ["de", "en"];

    public static bool IsSupported(string? language) =>
        language is not null && All.Contains(language, StringComparer.OrdinalIgnoreCase);
}
