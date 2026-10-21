using LittleSister.Api.Security;
using LittleSister.Core.Cameras;
using LittleSister.Core.Recording;
using LittleSister.Core.Settings;
using LittleSister.Core.Storage;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Recording;
using LittleSister.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace LittleSister.Api.Endpoints;

/// <summary>Kameras verwalten (Admin) und ansehen (alle Rollen).</summary>
internal static class CameraEndpoints
{
    public static void MapCameraEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/cameras").WithTags("Cameras");
        group.MapGet("", List).RequireAuthorization(Policies.Viewer);
        group.MapGet("/{id:guid}", Get).RequireAuthorization(Policies.Viewer);
        group.MapPost("", Create).RequireAuthorization(Policies.Admin);
        group.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.Admin);
        group.MapDelete("/{id:guid}", Delete).RequireAuthorization(Policies.Admin);
        group.MapPost("/test-stream", TestStream).RequireAuthorization(Policies.Admin);
    }

    /// <summary>Eingaben beim Anlegen/Ändern. Passwort null beim Ändern = unverändert lassen.</summary>
    internal sealed record CameraRequest(
        string Name,
        string MainStreamUrl,
        string? SubStreamUrl,
        string? Username,
        string? Password,
        string? OnvifAddress,
        string? Manufacturer,
        string? Model,
        bool RecordingEnabled,
        double QuotaGb,
        int? MaxRetentionDays);

    internal sealed record StorageUsage(long UsedBytes, int SegmentCount, DateTimeOffset? OldestUtc, DateTimeOffset? NewestUtc, double? EstimatedDays);

    internal sealed record CameraStatusDto(string State, DateTimeOffset SinceUtc, string? LastError, int Restarts, DateTimeOffset? LastSegmentUtc);

    /// <summary>Kamera für die Oberfläche; das Passwort wird nie ausgeliefert, nur ob eines gesetzt ist.</summary>
    internal sealed record CameraDto(
        Guid Id, string Name, string MainStreamUrl, string? SubStreamUrl, string? Username, bool HasPassword,
        string? OnvifAddress, string? Manufacturer, string? Model, bool RecordingEnabled, double QuotaGb, int? MaxRetentionDays,
        CameraStatusDto Status, StorageUsage Storage);

    internal sealed record TestStreamRequest(string Url, string? Username, string? Password, Guid? CameraId);

    private static async Task<IReadOnlyList<CameraDto>> List(LittleSisterDbContext db, CameraStatusRegistry statuses)
    {
        var cameras = await db.Cameras.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        var usage = await UsageAsync(db, null);
        return [.. cameras.Select(c => ToDto(c, statuses, usage.GetValueOrDefault(c.Id)))];
    }

    private static async Task<IResult> Get(Guid id, LittleSisterDbContext db, CameraStatusRegistry statuses)
    {
        var camera = await db.Cameras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (camera is null)
            return Results.NotFound();
        var usage = await UsageAsync(db, id);
        return Results.Ok(ToDto(camera, statuses, usage.GetValueOrDefault(id)));
    }

    private static async Task<IResult> Create(
        CameraRequest request, LittleSisterDbContext db, CameraCredentialProtector credentials,
        IRecordingControl recording, CameraStatusRegistry statuses, TimeProvider time)
    {
        var (normalized, errors) = Normalize(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var camera = new Camera { CreatedAtUtc = time.GetUtcNow() };
        Apply(camera, normalized, credentials, keepPasswordIfNull: false);
        db.Cameras.Add(camera);
        await db.SaveChangesAsync();

        recording.RequestReload();
        return Results.Created($"/api/cameras/{camera.Id}", ToDto(camera, statuses, null));
    }

    private static async Task<IResult> Update(
        Guid id, CameraRequest request, LittleSisterDbContext db, CameraCredentialProtector credentials,
        IRecordingControl recording, CameraStatusRegistry statuses)
    {
        var camera = await db.Cameras.FindAsync(id);
        if (camera is null)
            return Results.NotFound();

        var (normalized, errors) = Normalize(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        Apply(camera, normalized, credentials, keepPasswordIfNull: true);
        await db.SaveChangesAsync();

        recording.RequestReload();
        var usage = await UsageAsync(db, id);
        return Results.Ok(ToDto(camera, statuses, usage.GetValueOrDefault(id)));
    }

    /// <param name="deleteRecordings">true: auch alle Aufnahmen der Kamera von der Platte löschen.</param>
    private static async Task<IResult> Delete(
        Guid id, bool? deleteRecordings, LittleSisterDbContext db, IRecordingControl recording, CameraStatusRegistry statuses)
    {
        var camera = await db.Cameras.FindAsync(id);
        if (camera is null)
            return Results.NotFound();

        // Alle Ordner merken, in denen diese Kamera je aufgenommen hat (Aufnahmeordner kann sich geändert haben)
        var settings = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync();
        var roots = await db.Segments.Where(s => s.CameraId == id).Select(s => s.StorageRoot).Distinct().ToListAsync();
        if (!string.IsNullOrEmpty(settings?.RecordingsPath))
            roots.Add(settings.RecordingsPath);

        db.Cameras.Remove(camera); // Segmente werden per Fremdschlüssel mitgelöscht
        await db.SaveChangesAsync();
        recording.RequestReload();
        statuses.Remove(id);

        if (deleteRecordings == true)
        {
            var folders = roots.Where(r => r.Length > 0).Distinct().Select(r => SegmentFiles.CameraFolder(r, id)).ToList();
            // Kurz warten, bis der Recorder ffmpeg beendet hat, dann aufräumen
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(15));
                foreach (var folder in folders)
                {
                    try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Bleibt liegen, schadet aber nicht */ }
                }
            });
        }
        return Results.NoContent();
    }

    /// <summary>Prüft einen Stream per ffprobe; mit CameraId wird das gespeicherte Passwort verwendet.</summary>
    private static async Task<IResult> TestStream(
        TestStreamRequest request, LittleSisterDbContext db, CameraCredentialProtector credentials, StreamProbe probe, CancellationToken ct)
    {
        var url = StreamUrl.WithoutCredentials(request.Url?.Trim() ?? "", out var urlUser, out var urlPassword);
        if (!CameraValidator.IsRtspUrl(url))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["url"] = ["rtspUrlRequired"] });

        var username = request.Username ?? urlUser;
        var password = request.Password ?? urlPassword;
        if (password is null && request.CameraId is { } cameraId)
        {
            var camera = await db.Cameras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cameraId, ct);
            password = credentials.Unprotect(camera?.ProtectedPassword);
        }

        return Results.Ok(await probe.ProbeAsync(url, username, password, ct));
    }

    /// <summary>Validiert und trennt Zugangsdaten ab, falls sie direkt in der URL stehen.</summary>
    private static (CameraRequest Request, Dictionary<string, string[]> Errors) Normalize(CameraRequest r)
    {
        var main = StreamUrl.WithoutCredentials(r.MainStreamUrl?.Trim() ?? "", out var user, out var password);
        var sub = string.IsNullOrWhiteSpace(r.SubStreamUrl) ? null : StreamUrl.WithoutCredentials(r.SubStreamUrl.Trim(), out _, out _);

        var normalized = r with
        {
            Name = r.Name?.Trim() ?? "",
            MainStreamUrl = main,
            SubStreamUrl = sub,
            Username = string.IsNullOrWhiteSpace(r.Username) ? user : r.Username.Trim(),
            Password = r.Password ?? password,
        };
        return (normalized, CameraValidator.Validate(normalized.Name, main, sub, r.QuotaGb, r.MaxRetentionDays));
    }

    private static void Apply(Camera camera, CameraRequest r, CameraCredentialProtector credentials, bool keepPasswordIfNull)
    {
        camera.Name = r.Name;
        camera.MainStreamUrl = r.MainStreamUrl;
        camera.SubStreamUrl = r.SubStreamUrl;
        camera.Username = r.Username;
        if (!keepPasswordIfNull || r.Password is not null)
            camera.ProtectedPassword = credentials.Protect(r.Password);
        camera.OnvifAddress = string.IsNullOrWhiteSpace(r.OnvifAddress) ? null : r.OnvifAddress.Trim();
        camera.Manufacturer = r.Manufacturer;
        camera.Model = r.Model;
        camera.RecordingEnabled = r.RecordingEnabled;
        camera.QuotaGb = r.QuotaGb;
        camera.MaxRetentionDays = r.MaxRetentionDays;
    }

    private static CameraDto ToDto(Camera c, CameraStatusRegistry statuses, StorageUsage? usage)
    {
        var s = statuses.Get(c.Id);
        return new CameraDto(
            c.Id, c.Name, c.MainStreamUrl, c.SubStreamUrl, c.Username, c.ProtectedPassword is not null,
            c.OnvifAddress, c.Manufacturer, c.Model, c.RecordingEnabled, c.QuotaGb, c.MaxRetentionDays,
            new CameraStatusDto(c.RecordingEnabled ? s.State.ToString() : nameof(CameraState.Idle), s.SinceUtc, s.LastError, s.Restarts, s.LastSegmentUtc),
            usage is null ? new StorageUsage(0, 0, null, null, null) : usage with
            {
                EstimatedDays = usage.OldestUtc is { } from && usage.NewestUtc is { } to
                    ? RetentionPolicy.EstimateDays(usage.UsedBytes, to - from, c.QuotaBytes)
                    : null,
            });
    }

    /// <summary>Speicherbelegung je Kamera per SQL-Aggregation (ohne alle Segmente zu laden).</summary>
    private static async Task<Dictionary<Guid, StorageUsage>> UsageAsync(LittleSisterDbContext db, Guid? cameraId)
    {
        IQueryable<Segment> query = db.Segments;
        if (cameraId is { } id)
            query = query.Where(s => s.CameraId == id);

        var rows = await query
            .GroupBy(s => s.CameraId)
            .Select(g => new { CameraId = g.Key, Used = g.Sum(s => s.SizeBytes), Count = g.Count() })
            .ToListAsync();

        var result = new Dictionary<Guid, StorageUsage>();
        foreach (var row in rows)
        {
            // Min/Max über DateTimeOffset kann SQLite nicht übersetzen, daher gezielt ältestes und neuestes Segment holen
            var oldest = await db.Segments.Where(s => s.CameraId == row.CameraId).OrderBy(s => s.StartUtc).Select(s => (DateTimeOffset?)s.StartUtc).FirstOrDefaultAsync();
            var newest = await db.Segments.Where(s => s.CameraId == row.CameraId).OrderByDescending(s => s.StartUtc).Select(s => (DateTimeOffset?)s.EndUtc).FirstOrDefaultAsync();
            result[row.CameraId] = new StorageUsage(row.Used, row.Count, oldest, newest, null);
        }
        return result;
    }
}
