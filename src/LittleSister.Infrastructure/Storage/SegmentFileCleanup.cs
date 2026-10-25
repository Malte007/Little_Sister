using LittleSister.Core.Recording;
using LittleSister.Infrastructure.Playback;
using Microsoft.Extensions.Logging;

namespace LittleSister.Infrastructure.Storage;

/// <summary>Entfernt die Dateien eines Segments (Video, Vorschaubilder, leeren Tagesordner).</summary>
public static class SegmentFileCleanup
{
    public static void Delete(Segment segment, ILogger logger)
    {
        try
        {
            // Jedes Segment liegt in dem Ordner, der bei seiner Aufnahme galt
            File.Delete(segment.FullPath);
            File.Delete(SegmentThumbnails.PathOf(segment));
            DeleteEmptyDirectory(Path.GetDirectoryName(segment.FullPath)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Datei {Path} konnte nicht gelöscht werden", segment.FullPath);
        }
    }

    /// <summary>Entfernt leere Tagesordner, damit sich keine Verzeichnisleichen ansammeln.</summary>
    private static void DeleteEmptyDirectory(string directory)
    {
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);
    }
}
