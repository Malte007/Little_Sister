using Microsoft.AspNetCore.DataProtection;

namespace LittleSister.Infrastructure.Security;

/// <summary>
/// Verschlüsselt Kamera-Passwörter für die Datenbank. Wir brauchen sie im Klartext für RTSP,
/// deshalb Verschlüsselung statt Hash; der Schlüssel liegt getrennt von der Datenbank.
/// </summary>
public sealed class CameraCredentialProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("LittleSister.CameraCredentials.v1");

    public string? Protect(string? password) =>
        string.IsNullOrEmpty(password) ? null : protector.Protect(password);

    public string? Unprotect(string? protectedPassword) =>
        string.IsNullOrEmpty(protectedPassword) ? null : protector.Unprotect(protectedPassword);
}
