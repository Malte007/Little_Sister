using Microsoft.AspNetCore.Identity;

namespace LittleSister.Infrastructure.Identity;

/// <summary>Benutzerkonto; Passwörter speichert ASP.NET Identity nur als Hash.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAtUtc { get; set; }
}
