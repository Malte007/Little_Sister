using System.Threading.RateLimiting;
using LittleSister.Api.Security;
using LittleSister.Api.Storage;
using LittleSister.Core.Security;
using LittleSister.Infrastructure;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace LittleSister.Api;

internal static class ApiServiceCollectionExtensions
{
    /// <summary>Login, Rollen, Schutz gegen Brute-Force und sonstige Web-Dienste.</summary>
    public static IServiceCollection AddLittleSisterApi(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(LittleSisterOptions.SectionName).Get<LittleSisterOptions>() ?? new();

        services.AddProblemDetails();
        services.AddOpenApi();

        // Schlüssel für Cookies/Tokens (und später Kamera-Zugangsdaten) getrennt von der Datenbank ablegen
        services.AddDataProtection()
            .SetApplicationName("LittleSister")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(options.GetFullDataPath(), "keys")));

        // Cookie für die Weboberfläche, Bearer-Token für spätere Apps
        services.AddIdentityApiEndpoints<AppUser>(o =>
            {
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;

                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);

                o.User.RequireUniqueEmail = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<LittleSisterDbContext>();

        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "littlesister.auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            // Im LAN ist HTTP erlaubt; hinter HTTPS wird das Cookie automatisch als "Secure" markiert
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromDays(14);
            o.SlidingExpiration = true;

            // Eine API leitet nicht auf eine Login-Seite um, sondern antwortet mit 401/403
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin))
            .AddPolicy(Policies.Viewer, p => p.RequireRole(Roles.Admin, Roles.Viewer));

        // Höchstens 10 Login-/Einrichtungsversuche pro Minute und IP-Adresse
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(Policies.LoginRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
        });

        services.AddSingleton<StoragePathChecker>();
        return services;
    }
}
