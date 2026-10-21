using LittleSister.Api.Security;
using LittleSister.Core.Settings;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace LittleSister.Api.Endpoints;

/// <summary>Überblick über den Aufnahmespeicher und die globale Freispeichergrenze.</summary>
internal static class StorageEndpoints
{
    public static void MapStorageEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/storage").WithTags("Storage");
        group.MapGet("", Get).RequireAuthorization(Policies.Viewer);
        group.MapPut("", Update).RequireAuthorization(Policies.Admin);
    }

    internal sealed record StorageInfo(string RecordingsPath, long? FreeBytes, long? TotalBytes, long UsedBytes, double MinFreeGb);
    internal sealed record StorageUpdate(double MinFreeGb);

    private static async Task<IResult> Get(LittleSisterDbContext db, IFreeSpaceProvider freeSpace)
    {
        var settings = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == SystemSettings.SingletonId);
        if (settings is null)
            return Results.NotFound();

        long? total = null;
        try
        {
            if (Directory.Exists(settings.RecordingsPath))
                total = new DriveInfo(settings.RecordingsPath).TotalSize;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }

        var used = await db.Segments.SumAsync(s => s.SizeBytes);
        return Results.Ok(new StorageInfo(settings.RecordingsPath, freeSpace.GetAvailableBytes(settings.RecordingsPath), total, used, settings.MinFreeGb));
    }

    private static async Task<IResult> Update(StorageUpdate request, LittleSisterDbContext db)
    {
        if (request.MinFreeGb < 0)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["minFreeGb"] = ["tooSmall"] });

        var settings = await db.SystemSettings.FindAsync(SystemSettings.SingletonId);
        if (settings is null)
            return Results.NotFound();
        settings.MinFreeGb = request.MinFreeGb;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
