using LittleSister.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LittleSister.Infrastructure.Storage;

/// <summary>
/// Kennungsdatei im Aufnahmeordner. Fehlt sie, ist vermutlich die Freigabe bzw. Platte nicht eingehängt
/// und der Ordner nur der leere Einhängepunkt: Dann wird weder aufgenommen noch gelöscht.
/// </summary>
public static class StorageMarker
{
    public const string FileName = ".little-sister";

    /// <summary>So viele der ältesten Aufnahmen werden geprüft, bevor bei einer älteren Installation die Kennung entsteht.</summary>
    private const int ProbeCount = 20;

    public static string PathOf(string root) => Path.Combine(root, FileName);

    /// <summary>Legt die Kennung an (beim Einrichten oder Wechseln des Aufnahmeordners).</summary>
    public static void Write(string root) =>
        File.WriteAllText(PathOf(root),
            "Aufnahmeordner von Little Sister. Nicht löschen: Ohne diese Datei gilt der Ordner als nicht eingehängt.\n");

    /// <summary>
    /// Ist der Ordner der eingerichtete Aufnahmeordner? Ohne Kennung (Installation von vor dieser Prüfung)
    /// wird sie nur angelegt, wenn noch keine Aufnahmen bekannt sind oder eine der ältesten tatsächlich dort liegt.
    /// </summary>
    public static async Task<bool> IsAvailableAsync(LittleSisterDbContext db, string root, CancellationToken ct)
    {
        if (File.Exists(PathOf(root)))
            return true;
        if (!Directory.Exists(root))
            return false;

        var oldest = await db.Segments.AsNoTracking()
            .Where(s => s.StorageRoot == root)
            .OrderBy(s => s.StartUtc).Take(ProbeCount).ToListAsync(ct);
        if (oldest.Count > 0 && !oldest.Any(s => File.Exists(s.FullPath)))
            return false;

        try
        {
            Write(root);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
