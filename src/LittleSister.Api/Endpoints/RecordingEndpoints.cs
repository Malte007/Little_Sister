using LittleSister.Api.Security;
using LittleSister.Core.Recording;
using LittleSister.Core.Settings;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Playback;
using LittleSister.Infrastructure.Recording;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace LittleSister.Api.Endpoints;

/// <summary>Aufnahmen ansehen: Tagesübersicht für die Zeitleiste, HLS-Wiedergabe und Export.</summary>
internal static class RecordingEndpoints
{
    public static void MapRecordingEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/recordings").WithTags("Recordings").RequireAuthorization(Policies.Viewer);
        group.MapGet("/{id:guid}/day", Day);
        group.MapGet("/{id:guid}/day/{date}/index.m3u8", Playlist);
        group.MapGet("/segments/{segmentId:long}.mp4", SegmentFile);
        group.MapGet("/segments/{segmentId:long}/thumbnails.jpg", Thumbnails);
        group.MapGet("/{id:guid}/export", Export);
    }

    /// <param name="Thumbnails">Anzahl der Vorschaubilder im Bildstreifen des Segments, eines je <see cref="DayDto.ThumbnailSeconds"/>.</param>
    internal sealed record SegmentDto(long Id, DateTimeOffset StartUtc, DateTimeOffset EndUtc, int Thumbnails);

    /// <summary>Ereignismarker auf der Zeitleiste (z. B. Bewegung); folgen mit der ereignisgesteuerten Aufnahme.</summary>
    internal sealed record EventDto(DateTimeOffset TimeUtc, string Kind);

    /// <param name="Segments">Abspielbare Segmente in Playlist-Reihenfolge; die Oberfläche rechnet damit Uhrzeit und Abspielposition um.</param>
    /// <param name="PreviousDate">Letzter Tag davor mit Aufnahmen, für die Blätter-Knöpfe.</param>
    /// <param name="ThumbnailSeconds">Abstand der Vorschaubilder in Sekunden; 0 = Vorschaubilder ausgeschaltet.</param>
    internal sealed record DayDto(
        DateOnly Date, DateOnly Today, string TimeZoneId, DateTimeOffset FromUtc, DateTimeOffset ToUtc,
        IReadOnlyList<SegmentDto> Segments, IReadOnlyList<EventDto> Events, DateOnly? PreviousDate, DateOnly? NextDate,
        int ThumbnailSeconds);

    /// <param name="date">Tag in der Zeitzone der Installation; ohne Angabe heute.</param>
    private static async Task<IResult> Day(
        Guid id, DateOnly? date, LittleSisterDbContext db, SegmentThumbnails thumbnails, TimeProvider time, CancellationToken ct)
    {
        if (!await db.Cameras.AnyAsync(c => c.Id == id, ct))
            return Results.NotFound();

        var zoneId = await TimeZoneIdAsync(db, ct);
        var zone = RecordingDay.Zone(zoneId);
        var today = RecordingDay.DateOf(time.GetUtcNow(), zone);
        var day = RecordingDay.Of(date ?? today, zone);
        var segments = await PlayableSegmentsAsync(db, id, day, ct);

        var previous = await db.Segments.Where(s => s.CameraId == id && s.StartUtc < day.FromUtc)
            .OrderByDescending(s => s.StartUtc).Select(s => (DateTimeOffset?)s.StartUtc).FirstOrDefaultAsync(ct);
        var next = await db.Segments.Where(s => s.CameraId == id && s.StartUtc >= day.ToUtc)
            .OrderBy(s => s.StartUtc).Select(s => (DateTimeOffset?)s.StartUtc).FirstOrDefaultAsync(ct);

        return Results.Ok(new DayDto(
            day.Date, today, zone.Id, day.FromUtc, day.ToUtc,
            segments.Select(s => new SegmentDto(s.Id, s.StartUtc, s.EndUtc, SegmentThumbnails.CountOf(s))).ToList(), [],
            previous is { } p ? RecordingDay.DateOf(p, zone) : null,
            next is { } n ? RecordingDay.DateOf(n, zone) : null,
            await thumbnails.IsEnabledAsync(ct) ? SegmentThumbnails.IntervalSeconds : 0));
    }

    private static async Task<IResult> Playlist(Guid id, DateOnly date, HttpContext http, LittleSisterDbContext db, CancellationToken ct)
    {
        if (!await db.Cameras.AnyAsync(c => c.Id == id, ct))
            return Results.NotFound();

        var day = RecordingDay.Of(date, RecordingDay.Zone(await TimeZoneIdAsync(db, ct)));
        var segments = await PlayableSegmentsAsync(db, id, day, ct);
        var basePath = $"{http.Request.PathBase}/api/recordings/segments/";
        var playlist = HlsPlaylist.Build(
            segments.Select(s => new PlaylistSegment(s.Id, s.StartUtc, s.EndUtc, s.InitBytes, s.SizeBytes)).ToList(),
            segmentId => $"{basePath}{segmentId}.mp4");

        // Heute wächst die Playlist noch; nicht zwischenspeichern
        http.Response.Headers.CacheControl = "no-store";
        return Results.Text(playlist, "application/vnd.apple.mpegurl");
    }

    private static async Task<IResult> SegmentFile(long segmentId, HttpContext http, LittleSisterDbContext db, CancellationToken ct)
    {
        var segment = await db.Segments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == segmentId, ct);
        if (segment is null || !File.Exists(segment.FullPath))
            return Results.NotFound();

        // Segmente ändern sich nach dem Schreiben nie mehr
        http.Response.Headers.CacheControl = "private, max-age=86400";
        return Results.File(segment.FullPath, "video/mp4", enableRangeProcessing: true);
    }

    /// <summary>Bildstreifen eines Segments; fehlt er noch (z. B. ältere Aufnahmen), wird er jetzt erzeugt,
    /// sofern Vorschaubilder eingeschaltet sind.</summary>
    private static async Task<IResult> Thumbnails(long segmentId, HttpContext http, LittleSisterDbContext db, SegmentThumbnails thumbnails, CancellationToken ct)
    {
        var segment = await db.Segments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == segmentId, ct);
        if (segment is null)
            return Results.NotFound();
        var exists = File.Exists(SegmentThumbnails.PathOf(segment))
            || (await thumbnails.IsEnabledAsync(ct) && await thumbnails.EnsureAsync(segment).WaitAsync(ct));
        if (!exists)
            return Results.NotFound();

        http.Response.Headers.CacheControl = "private, max-age=86400";
        return Results.File(SegmentThumbnails.PathOf(segment), "image/jpeg");
    }

    private static async Task<IResult> Export(
        Guid id, DateTimeOffset fromUtc, DateTimeOffset toUtc, HttpContext http, LittleSisterDbContext db, ClipExport export, CancellationToken ct)
    {
        if (toUtc <= fromUtc)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["toUtc"] = ["endBeforeStart"] });
        if (toUtc - fromUtc > ClipExport.MaxLength)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["toUtc"] = ["exportTooLong"] });

        var camera = await db.Cameras.AsNoTracking().Where(c => c.Id == id).Select(c => new { c.Name }).FirstOrDefaultAsync(ct);
        if (camera is null)
            return Results.NotFound();

        var segments = await db.Segments.AsNoTracking()
            .Where(s => s.CameraId == id && s.StartUtc < toUtc && s.EndUtc > fromUtc)
            .OrderBy(s => s.StartUtc).ToListAsync(ct);
        segments.RemoveAll(s => !File.Exists(s.FullPath));
        if (segments.Count == 0)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "noRecordings");

        var zone = RecordingDay.Zone(await TimeZoneIdAsync(db, ct));
        var start = TimeZoneInfo.ConvertTime(fromUtc, zone);
        http.Response.ContentType = "video/mp4";
        // filename* trägt Umlaute im Kameranamen, filename dient älteren Programmen als Ersatz
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName($"{SafeFileName(camera.Name)}_{start:yyyy-MM-dd_HH-mm-ss}.mp4");
        http.Response.Headers.ContentDisposition = disposition.ToString();

        var ok = await export.WriteAsync(segments, fromUtc, toUtc, http.Response.Body, ct);
        if (!ok && !http.Response.HasStarted)
        {
            http.Response.Headers.ContentDisposition = default;
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "exportFailed");
        }
        return Results.Empty;
    }

    /// <summary>
    /// Segmente eines Tages, die sich per HLS abspielen lassen. Fehlt die Größe des Dateikopfs
    /// (Segmente aus älteren Versionen), wird sie jetzt ermittelt und gespeichert.
    /// </summary>
    private static async Task<List<Segment>> PlayableSegmentsAsync(LittleSisterDbContext db, Guid cameraId, RecordingDay day, CancellationToken ct)
    {
        var segments = await db.Segments
            .Where(s => s.CameraId == cameraId && s.StartUtc < day.ToUtc && s.EndUtc > day.FromUtc)
            .OrderBy(s => s.StartUtc).ToListAsync(ct);

        var missing = segments.Where(s => s.InitBytes <= 0).ToList();
        foreach (var segment in missing)
            segment.InitBytes = Mp4Boxes.FindInitSize(segment.FullPath) ?? 0;
        if (missing.Count > 0)
            await db.SaveChangesAsync(ct);

        return segments.Where(s => s.InitBytes > 0 && s.InitBytes < s.SizeBytes).ToList();
    }

    private static async Task<string?> TimeZoneIdAsync(LittleSisterDbContext db, CancellationToken ct) =>
        await db.SystemSettings.AsNoTracking().Where(s => s.Id == SystemSettings.SingletonId).Select(s => s.TimeZoneId).FirstOrDefaultAsync(ct);

    /// <summary>Kameraname als Dateiname: Zeichen, die Dateisysteme nicht mögen, werden ersetzt.</summary>
    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var safe = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim();
        return safe.Length == 0 ? "kamera" : safe;
    }
}
