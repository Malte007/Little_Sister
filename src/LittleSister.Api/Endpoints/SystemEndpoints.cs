using System.Reflection;
using LittleSister.Api.Security;
using LittleSister.Core.Settings;
using LittleSister.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LittleSister.Api.Endpoints;

/// <summary>Allgemeine Informationen zur Installation.</summary>
internal static class SystemEndpoints
{
    public static void MapSystemEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/system/info", Info).RequireAuthorization(Policies.Viewer).WithTags("System");
    }

    internal sealed record SystemInfo(string Version, string Language, string TimeZoneId);

    private static async Task<SystemInfo> Info(LittleSisterDbContext db)
    {
        var settings = await db.SystemSettings.FindAsync(SystemSettings.SingletonId) ?? new SystemSettings();
        var version = typeof(SystemEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unbekannt";
        return new SystemInfo(version, settings.Language, settings.TimeZoneId);
    }
}
