namespace LittleSister.Core.Cameras;

/// <summary>Prüft Kameradaten, bevor sie gespeichert werden.</summary>
public static class CameraValidator
{
    public const double MinQuotaGb = 1;

    public static readonly int[] Rotations = [0, 90, 180, 270];

    /// <summary>Grenzen für Vor- und Nachlauf in Sekunden.</summary>
    public const int MaxPreRollSeconds = 60;
    public const int MaxPostRollSeconds = 600;

    public static Dictionary<string, string[]> Validate(
        string? name, string? mainStreamUrl, string? subStreamUrl, double quotaGb, int? maxRetentionDays, int rotation = 0,
        RecordingMode mode = RecordingMode.Continuous, string? onvifAddress = null, int preRollSeconds = 10, int postRollSeconds = 30)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
            errors["name"] = ["required"];

        if (!IsRtspUrl(mainStreamUrl))
            errors["mainStreamUrl"] = ["rtspUrlRequired"];

        if (!string.IsNullOrWhiteSpace(subStreamUrl) && !IsRtspUrl(subStreamUrl))
            errors["subStreamUrl"] = ["rtspUrlRequired"];

        if (quotaGb < MinQuotaGb)
            errors["quotaGb"] = ["tooSmall"];

        if (maxRetentionDays is < 1)
            errors["maxRetentionDays"] = ["tooSmall"];

        if (!Rotations.Contains(rotation))
            errors["rotation"] = ["invalidRotation"];

        // Bewegung meldet die Kamera per ONVIF; ohne ONVIF-Adresse gäbe es nie ein Ereignis
        if (mode == RecordingMode.Motion && string.IsNullOrWhiteSpace(onvifAddress))
            errors["recordingMode"] = ["motionNeedsOnvif"];

        if (preRollSeconds is < 0 or > MaxPreRollSeconds)
            errors["preRollSeconds"] = ["outOfRange"];

        if (postRollSeconds is < 0 or > MaxPostRollSeconds)
            errors["postRollSeconds"] = ["outOfRange"];

        return errors;
    }

    /// <summary>Nur rtsp:// bzw. rtsps:// mit Host; Zugangsdaten gehören nicht in die URL, sondern in eigene Felder.</summary>
    public static bool IsRtspUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == "rtsp" || uri.Scheme == "rtsps")
        && !string.IsNullOrEmpty(uri.Host);
}
