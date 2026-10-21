namespace LittleSister.Core.Cameras;

/// <summary>Prüft Kameradaten, bevor sie gespeichert werden.</summary>
public static class CameraValidator
{
    public const double MinQuotaGb = 1;

    public static readonly int[] Rotations = [0, 90, 180, 270];

    public static Dictionary<string, string[]> Validate(
        string? name, string? mainStreamUrl, string? subStreamUrl, double quotaGb, int? maxRetentionDays, int rotation = 0)
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

        return errors;
    }

    /// <summary>Nur rtsp:// bzw. rtsps:// mit Host; Zugangsdaten gehören nicht in die URL, sondern in eigene Felder.</summary>
    public static bool IsRtspUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == "rtsp" || uri.Scheme == "rtsps")
        && !string.IsNullOrEmpty(uri.Host);
}
