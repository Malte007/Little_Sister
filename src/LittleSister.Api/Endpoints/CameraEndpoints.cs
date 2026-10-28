using LittleSister.Api.Security;
using LittleSister.Core.Cameras;
using LittleSister.Core.Recording;
using LittleSister.Core.Settings;
using LittleSister.Core.Storage;
using LittleSister.Infrastructure;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Onvif;
using LittleSister.Infrastructure.Recording;
using LittleSister.Infrastructure.Security;
using LittleSister.Infrastructure.Storage;
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
        group.MapDelete("/{id:guid}/recordings", DeleteRecordings).RequireAuthorization(Policies.Admin);
        group.MapGet("/export", Export).RequireAuthorization(Policies.Admin);
        group.MapPost("/import", Import).RequireAuthorization(Policies.Admin);
        group.MapPost("/test-stream", TestStream).RequireAuthorization(Policies.Admin);
        group.MapPost("/snapshot", StreamSnapshot).RequireAuthorization(Policies.Admin);
    }

    /// <summary>Eingaben beim Anlegen/Ändern. Passwort null beim Ändern = unverändert lassen.</summary>
    /// <param name="RecordingEnabled">Ältere Form: nur ohne <paramref name="RecordingMode"/> ausgewertet (an = Dauerhaft).</param>
    /// <param name="RecordingMode">"Off", "Continuous" oder "Motion".</param>
    /// <param name="MotionDetection">"Video" (Standard) oder "Onvif".</param>
    /// <param name="MotionZones">Erkennungsbereiche als 16×12-Raster aus '0'/'1'; null = ganzes Bild.</param>
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
        int? MaxRetentionDays,
        int Rotation = 0,
        string? RecordingMode = null,
        int PreRollSeconds = 10,
        int PostRollSeconds = 30,
        string? MotionDetection = null,
        int MotionSensitivity = 50,
        string? MotionZones = null)
    {
        /// <summary>Gewählter Modus; null bei unbekanntem Wert.</summary>
        public RecordingMode? Mode => RecordingMode is null
            ? RecordingEnabled ? Core.Cameras.RecordingMode.Continuous : Core.Cameras.RecordingMode.Off
            : Enum.GetValues<RecordingMode>().Cast<RecordingMode?>()
                .FirstOrDefault(m => m.ToString()!.Equals(RecordingMode, StringComparison.OrdinalIgnoreCase));

        /// <summary>Gewählte Bewegungsquelle; null bei unbekanntem Wert.</summary>
        public MotionDetection? Detection => MotionDetection is null
            ? Core.Cameras.MotionDetection.Video
            : Enum.GetValues<MotionDetection>().Cast<MotionDetection?>()
                .FirstOrDefault(m => m.ToString()!.Equals(MotionDetection, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Exportdatei mit Kamera-Konfigurationen; ohne Aufnahmen und Ereignisse, Passwörter nur auf Wunsch (Klartext).</summary>
    /// <param name="Format">Immer <see cref="ExportFormat"/>, damit fremde Dateien erkannt werden.</param>
    /// <param name="Version">Version des Dateiformats; neuere Versionen werden abgelehnt.</param>
    internal sealed record CameraExportFile(string Format, int Version, DateTimeOffset ExportedAtUtc, IReadOnlyList<CameraRequest> Cameras);

    /// <param name="NeedsPassword">Benutzername gesetzt, aber die Datei enthielt kein Passwort.</param>
    internal sealed record ImportedCamera(Guid Id, string Name, bool NeedsPassword);

    internal const string ExportFormat = "little-sister-cameras";
    internal const int ExportVersion = 1;

    internal sealed record StorageUsage(long UsedBytes, int SegmentCount, DateTimeOffset? OldestUtc, DateTimeOffset? NewestUtc, double? EstimatedDays);

    /// <param name="Events">Verbindung zur Bewegungserkennung: None, Connecting, Connected, Error.</param>
    /// <param name="Motion">Läuft gerade eine Bewegung?</param>
    internal sealed record CameraStatusDto(
        string State, DateTimeOffset SinceUtc, string? LastError, int Restarts, DateTimeOffset? LastSegmentUtc,
        string Events, string? EventError, bool Motion, DateTimeOffset? LastMotionUtc)
    {
        public static CameraStatusDto From(CameraStatus s) => new(
            s.State.ToString(), s.SinceUtc, s.LastError, s.Restarts, s.LastSegmentUtc,
            s.Events.ToString(), s.EventError, s.Motion, s.LastMotionUtc);
    }

    /// <summary>Kamera für die Oberfläche; das Passwort wird nie ausgeliefert, nur ob eines gesetzt ist.</summary>
    internal sealed record CameraDto(
        Guid Id, string Name, string MainStreamUrl, string? SubStreamUrl, string? Username, bool HasPassword,
        string? OnvifAddress, string? Manufacturer, string? Model, bool RecordingEnabled, double QuotaGb, int? MaxRetentionDays,
        int Rotation, string RecordingMode, int PreRollSeconds, int PostRollSeconds, string MotionDetection, int MotionSensitivity,
        string? MotionZones, CameraStatusDto Status, StorageUsage Storage);

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
        CameraChanges changes, CameraStatusRegistry statuses, TimeProvider time)
    {
        var (normalized, errors) = Normalize(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var camera = new Camera { CreatedAtUtc = time.GetUtcNow() };
        Apply(camera, normalized, credentials, keepPasswordIfNull: false);
        db.Cameras.Add(camera);
        await db.SaveChangesAsync();

        changes.Notify();
        return Results.Created($"/api/cameras/{camera.Id}", ToDto(camera, statuses, null));
    }

    private static async Task<IResult> Update(
        Guid id, CameraRequest request, LittleSisterDbContext db, CameraCredentialProtector credentials,
        CameraChanges changes, CameraStatusRegistry statuses)
    {
        var camera = await db.Cameras.FindAsync(id);
        if (camera is null)
            return Results.NotFound();

        var (normalized, errors) = Normalize(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        Apply(camera, normalized, credentials, keepPasswordIfNull: true);
        await db.SaveChangesAsync();

        changes.Notify();
        var usage = await UsageAsync(db, id);
        return Results.Ok(ToDto(camera, statuses, usage.GetValueOrDefault(id)));
    }

    /// <param name="deleteRecordings">true: auch alle Aufnahmen der Kamera von der Platte löschen.</param>
    private static async Task<IResult> Delete(
        Guid id, bool? deleteRecordings, LittleSisterDbContext db, CameraChanges changes, CameraStatusRegistry statuses)
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
        changes.Notify();
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

    /// <summary>Konfiguration einer, mehrerer (<c>?id=…&amp;id=…</c>) oder aller Kameras als Exportdatei.</summary>
    /// <param name="includePasswords">true: Passwörter im Klartext mitexportieren, damit die Datei auf jedem Server sofort funktioniert.</param>
    private static async Task<IResult> Export(
        Guid[]? id, bool? includePasswords, LittleSisterDbContext db, CameraCredentialProtector credentials, TimeProvider time)
    {
        var query = db.Cameras.AsNoTracking();
        if (id is { Length: > 0 })
            query = query.Where(c => id.Contains(c.Id));
        var cameras = await query.OrderBy(c => c.Name).ToListAsync();
        if (id is { Length: > 0 } && cameras.Count != id.Distinct().Count())
            return Results.NotFound();

        var file = new CameraExportFile(ExportFormat, ExportVersion, time.GetUtcNow(), [.. cameras.Select(c => new CameraRequest(
            c.Name, c.MainStreamUrl, c.SubStreamUrl, c.Username, includePasswords == true ? credentials.Unprotect(c.ProtectedPassword) : null, c.OnvifAddress, c.Manufacturer, c.Model,
            c.RecordingEnabled, c.QuotaGb, c.MaxRetentionDays, c.Rotation, c.RecordingMode.ToString(), c.PreRollSeconds,
            c.PostRollSeconds, c.MotionDetection.ToString(), c.MotionSensitivity, c.MotionZones))]);
        return Results.Ok(file);
    }

    /// <summary>
    /// Legt die Kameras einer Exportdatei als neue Kameras an. Alles oder nichts: Ist eine Kamera ungültig, wird keine angelegt.
    /// Gleichnamige Kameras bekommen " (2)", " (3)" … angehängt.
    /// </summary>
    private static async Task<IResult> Import(
        CameraExportFile file, LittleSisterDbContext db, CameraCredentialProtector credentials, CameraChanges changes, TimeProvider time)
    {
        if (file.Format != ExportFormat)
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "importInvalidFile");
        if (file.Version > ExportVersion)
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "importNewerVersion");
        if (file.Cameras is not { Count: > 0 })
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "importEmpty");

        var errors = new Dictionary<string, string[]>();
        var normalized = new List<CameraRequest>();
        for (var i = 0; i < file.Cameras.Count; i++)
        {
            var (request, cameraErrors) = Normalize(file.Cameras[i]);
            foreach (var (field, codes) in cameraErrors)
                errors[$"cameras[{i}].{field}"] = codes;
            normalized.Add(request with { Password = string.IsNullOrEmpty(request.Password) ? null : request.Password });
        }
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var names = (await db.Cameras.Select(c => c.Name).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var imported = new List<ImportedCamera>();
        foreach (var request in normalized)
        {
            var name = UniqueName(request.Name, names);
            names.Add(name);
            var camera = new Camera { CreatedAtUtc = time.GetUtcNow() };
            Apply(camera, request with { Name = name }, credentials, keepPasswordIfNull: false);
            db.Cameras.Add(camera);
            imported.Add(new ImportedCamera(camera.Id, name, camera.Username is not null && request.Password is null));
        }
        await db.SaveChangesAsync();

        changes.Notify();
        return Results.Ok(imported);
    }

    /// <summary>Hängt " (2)", " (3)" … an, bis der Name frei ist.</summary>
    internal static string UniqueName(string name, IReadOnlySet<string> taken)
    {
        if (!taken.Contains(name))
            return name;
        for (var n = 2; ; n++)
            if (!taken.Contains($"{name} ({n})"))
                return $"{name} ({n})";
    }

    /// <param name="Deleted">Gelöschte Segmente.</param>
    /// <param name="Skipped">Segmente in nicht erreichbaren Aufnahmeordnern; sie bleiben, bis der Ordner wieder da ist.</param>
    internal sealed record DeleteRecordingsResult(int Deleted, int Skipped);

    /// <summary>Löscht alle Aufnahmen und abgeschlossenen Ereignisse einer Kamera; die Kamera selbst bleibt und nimmt weiter auf.</summary>
    private static async Task<IResult> DeleteRecordings(Guid id, LittleSisterDbContext db, ILoggerFactory loggers, CancellationToken ct)
    {
        var logger = loggers.CreateLogger(typeof(CameraEndpoints));
        if (!await db.Cameras.AnyAsync(c => c.Id == id, ct))
            return Results.NotFound();

        var segments = await db.Segments.Where(s => s.CameraId == id).ToListAsync(ct);
        // Ohne erreichbaren Ordner würden nur Datenbankeinträge verschwinden und die Dateien beim nächsten Start wieder eingelesen
        var available = new Dictionary<string, bool>();
        foreach (var root in segments.Select(s => s.StorageRoot).Distinct())
            available[root] = await StorageMarker.IsAvailableAsync(db, root, ct);
        var delete = segments.Where(s => available[s.StorageRoot]).ToList();

        // Erst die Einträge, dann die Dateien: so zeigt die Oberfläche nie Aufnahmen ohne Datei
        db.Segments.RemoveRange(delete);
        await db.SaveChangesAsync(ct);
        await db.Events.Where(e => e.CameraId == id && e.EndUtc != null).ExecuteDeleteAsync(ct);
        foreach (var segment in delete)
            SegmentFileCleanup.Delete(segment, logger);

        logger.LogInformation("Alle Aufnahmen der Kamera {Camera} gelöscht: {Count} Segment(e)", id, delete.Count);
        return Results.Ok(new DeleteRecordingsResult(delete.Count, segments.Count - delete.Count));
    }

    /// <summary>Prüft einen Stream per ffprobe; mit CameraId wird das gespeicherte Passwort verwendet.</summary>
    private static async Task<IResult> TestStream(
        TestStreamRequest request, LittleSisterDbContext db, CameraCredentialProtector credentials, StreamProbe probe, CancellationToken ct)
    {
        var target = await ResolveStreamAsync(request, db, credentials, ct);
        if (target is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["url"] = ["rtspUrlRequired"] });
        return Results.Ok(await probe.ProbeAsync(target.Value.Url, target.Value.Username, target.Value.Password, ct));
    }

    /// <summary>Standbild direkt vom eingegebenen Stream, auch für noch nicht gespeicherte Kameras.</summary>
    private static async Task<IResult> StreamSnapshot(
        TestStreamRequest request, LittleSisterDbContext db, CameraCredentialProtector credentials, StreamProbe probe, CancellationToken ct)
    {
        var target = await ResolveStreamAsync(request, db, credentials, ct);
        if (target is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["url"] = ["rtspUrlRequired"] });

        var (jpeg, error) = await probe.SnapshotAsync(target.Value.Url, target.Value.Username, target.Value.Password, ct);
        return jpeg is null
            ? Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: error)
            : Results.File(jpeg, "image/jpeg");
    }

    /// <summary>
    /// Adresse und Zugangsdaten für eine Stream-Prüfung; Zugangsdaten in der URL werden abgetrennt.
    /// Ohne neues Passwort gilt das gespeicherte der Kamera. null = keine gültige RTSP-Adresse.
    /// </summary>
    private static async Task<(string Url, string? Username, string? Password)?> ResolveStreamAsync(
        TestStreamRequest request, LittleSisterDbContext db, CameraCredentialProtector credentials, CancellationToken ct)
    {
        var url = StreamUrl.WithoutCredentials(request.Url?.Trim() ?? "", out var urlUser, out var urlPassword);
        if (!CameraValidator.IsRtspUrl(url))
            return null;

        var username = request.Username ?? urlUser;
        var password = request.Password ?? urlPassword;
        if (password is null && request.CameraId is { } cameraId)
        {
            var camera = await db.Cameras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cameraId, ct);
            password = credentials.Unprotect(camera?.ProtectedPassword);
        }
        return (url, username, password);
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
            // "http://IP:Port" genügt; der Pfad zum Gerätedienst wird ergänzt
            OnvifAddress = string.IsNullOrWhiteSpace(r.OnvifAddress) ? null : OnvifClient.NormalizeDeviceAddress(r.OnvifAddress),
            // Alle Felder aktiv heißt dasselbe wie keine Bereiche: ganzes Bild
            MotionZones = string.IsNullOrWhiteSpace(r.MotionZones) || r.MotionZones.All(c => c == '1') ? null : r.MotionZones.Trim(),
        };
        var errors = CameraValidator.Validate(normalized.Name, main, sub, r.QuotaGb, r.MaxRetentionDays, r.Rotation,
            r.Mode ?? RecordingMode.Off, normalized.OnvifAddress, r.PreRollSeconds, r.PostRollSeconds,
            r.Detection ?? MotionDetection.Video, r.MotionSensitivity, normalized.MotionZones);
        if (r.Mode is null)
            errors["recordingMode"] = ["invalidRecordingMode"];
        if (r.Detection is null)
            errors["motionDetection"] = ["invalidMotionDetection"];
        return (normalized, errors);
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
        camera.RecordingMode = r.Mode!.Value;
        camera.PreRollSeconds = r.PreRollSeconds;
        camera.PostRollSeconds = r.PostRollSeconds;
        camera.MotionDetection = r.Detection!.Value;
        camera.MotionSensitivity = r.MotionSensitivity;
        camera.MotionZones = r.MotionZones;
        camera.QuotaGb = r.QuotaGb;
        camera.MaxRetentionDays = r.MaxRetentionDays;
        camera.Rotation = r.Rotation;
    }

    private static CameraDto ToDto(Camera c, CameraStatusRegistry statuses, StorageUsage? usage)
    {
        var s = statuses.Get(c.Id);
        return new CameraDto(
            c.Id, c.Name, c.MainStreamUrl, c.SubStreamUrl, c.Username, c.ProtectedPassword is not null,
            c.OnvifAddress, c.Manufacturer, c.Model, c.RecordingEnabled, c.QuotaGb, c.MaxRetentionDays, c.Rotation,
            c.RecordingMode.ToString(), c.PreRollSeconds, c.PostRollSeconds, c.MotionDetection.ToString(), c.MotionSensitivity, c.MotionZones,
            CameraStatusDto.From(c.RecordingEnabled ? s : s with { State = CameraState.Idle }),
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
