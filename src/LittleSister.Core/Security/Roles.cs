namespace LittleSister.Core.Security;

/// <summary>Die festen Benutzerrollen von Little Sister.</summary>
public static class Roles
{
    /// <summary>Darf Kameras hinzufügen, Einstellungen ändern und alles ansehen.</summary>
    public const string Admin = "Admin";

    /// <summary>Darf Live-Bilder und Aufnahmen ansehen und herunterladen.</summary>
    public const string Viewer = "Viewer";

    public static IReadOnlyList<string> All { get; } = [Admin, Viewer];

    /// <summary>Prüft, ob der Name einer bekannten Rolle entspricht (Groß-/Kleinschreibung egal).</summary>
    public static bool TryNormalize(string? role, out string normalized)
    {
        normalized = All.FirstOrDefault(r => string.Equals(r, role?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
        return normalized.Length > 0;
    }
}
