using LittleSister.Api.Security;
using LittleSister.Api.Storage;
using LittleSister.Infrastructure.Recording;
using LittleSister.Core.Settings;
using LittleSister.Infrastructure;
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
    /// <param name="RecordingsPath">Neuer Aufnahmeordner; null = unverändert.</param>
    internal sealed record StorageUpdate(double MinFreeGb, string? RecordingsPath = null);

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

    /// <summary>
    /// Ändert Freispeichergrenze und Aufnahmeordner. Neue Aufnahmen landen im neuen Ordner;
    /// bestehende bleiben im alten Ordner und werden dort weiter vom Ringspeicher verwaltet.
    /// </summary>
    private static async Task<IResult> Update(
        StorageUpdate request, LittleSisterDbContext db, StoragePathChecker storage, CameraChanges changes)
    {
        if (request.MinFreeGb < 0)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["minFreeGb"] = ["tooSmall"] });

        var settings = await db.SystemSettings.FindAsync(SystemSettings.SingletonId);
        if (settings is null)
            return Results.NotFound();

        var newPath = request.RecordingsPath?.Trim();
        var pathChanged = !string.IsNullOrEmpty(newPath) && newPath != settings.RecordingsPath;
        if (pathChanged)
        {
            if (!Path.IsPathFullyQualified(newPath!))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["recordingsPath"] = ["absolutePathRequired"] });
            if (storage.Check(newPath!) is { } error)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["recordingsPath"] = [error] });
            settings.RecordingsPath = newPath!;
        }

        settings.MinFreeGb = request.MinFreeGb;
        await db.SaveChangesAsync();

        // Recorder mit neuem Ordner neu starten; das laufende Segment wird vorher sauber abgeschlossen
        if (pathChanged)
            changes.Notify();
        return Results.NoContent();
    }
}
