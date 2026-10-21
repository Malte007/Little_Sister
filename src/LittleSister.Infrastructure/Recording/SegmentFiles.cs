using System.Globalization;

namespace LittleSister.Infrastructure.Recording;

/// <summary>
/// Ablage der Segmente: ffmpeg schreibt nach &lt;Kamera&gt;/.incoming/,
/// fertige Segmente wandern nach &lt;Kamera&gt;/&lt;yyyyMMdd&gt;/&lt;HHmmss&gt;.mp4 (alle Zeiten UTC).
/// </summary>
public static class SegmentFiles
{
    public const string IncomingFolder = ".incoming";

    /// <summary>Dateimuster für ffmpeg (strftime, mit TZ=UTC gestartet).</summary>
    public const string IncomingPattern = "%Y%m%dT%H%M%SZ.mp4";

    private const string IncomingFormat = "yyyyMMdd'T'HHmmss'Z'";

    public static string CameraFolder(string root, Guid cameraId) => Path.Combine(root, cameraId.ToString("N"));

    public static string IncomingDirectory(string root, Guid cameraId) => Path.Combine(CameraFolder(root, cameraId), IncomingFolder);

    /// <summary>Liest den Startzeitpunkt aus einem Dateinamen wie "20261021T120420Z.mp4".</summary>
    public static bool TryParseStart(string fileName, out DateTimeOffset startUtc) =>
        DateTimeOffset.TryParseExact(
            Path.GetFileNameWithoutExtension(fileName), IncomingFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out startUtc);

    /// <summary>Endgültiger Ablageort relativ zum Aufnahmeordner.</summary>
    public static string FinalRelativePath(Guid cameraId, DateTimeOffset startUtc) =>
        Path.Combine(cameraId.ToString("N"), startUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            startUtc.ToString("HHmmss", CultureInfo.InvariantCulture) + ".mp4");
}
