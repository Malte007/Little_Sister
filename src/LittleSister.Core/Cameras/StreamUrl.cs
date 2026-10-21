namespace LittleSister.Core.Cameras;

/// <summary>Hilfen rund um RTSP-Adressen und Zugangsdaten.</summary>
public static class StreamUrl
{
    /// <summary>Setzt Benutzer und Passwort (URL-kodiert) in die Adresse ein, wie ffmpeg es erwartet.</summary>
    public static string WithCredentials(string url, string? username, string? password)
    {
        if (string.IsNullOrEmpty(username))
            return url;

        var builder = new UriBuilder(url)
        {
            UserName = Uri.EscapeDataString(username),
            Password = string.IsNullOrEmpty(password) ? "" : Uri.EscapeDataString(password),
        };
        return builder.Uri.ToString();
    }

    /// <summary>Entfernt Zugangsdaten aus einer Adresse, z. B. bevor sie gespeichert oder protokolliert wird.</summary>
    public static string WithoutCredentials(string url, out string? username, out string? password)
    {
        username = password = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo))
            return url;

        var parts = uri.UserInfo.Split(':', 2);
        username = Uri.UnescapeDataString(parts[0]);
        password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null;
        return new UriBuilder(uri) { UserName = "", Password = "" }.Uri.ToString();
    }
}
