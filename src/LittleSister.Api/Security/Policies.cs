namespace LittleSister.Api.Security;

/// <summary>Namen der Autorisierungs- und Drosselungsregeln.</summary>
internal static class Policies
{
    /// <summary>Nur Admins.</summary>
    public const string Admin = "admin";

    /// <summary>Jeder angemeldete Benutzer mit gültiger Rolle.</summary>
    public const string Viewer = "viewer";

    /// <summary>Drosselung für Login und Ersteinrichtung.</summary>
    public const string LoginRateLimit = "login";
}
