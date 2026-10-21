using System.Net.WebSockets;
using LittleSister.Api.Live;
using LittleSister.Api.Security;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Live;
using Microsoft.EntityFrameworkCore;

namespace LittleSister.Api.Endpoints;

/// <summary>Live-Ansicht: Player-Verbindung und Standbild, beides nur angemeldet und über Little Sister.</summary>
internal static class LiveEndpoints
{
    public static void MapLiveEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/live").WithTags("Live").RequireAuthorization(Policies.Viewer);
        group.MapGet("", (LiveStatus status) => status.Current);
        group.MapGet("/{id:guid}/ws", Connect);
        group.MapGet("/{id:guid}/snapshot", Snapshot);

        api.MapHub<StatusHub>("/hubs/status").RequireAuthorization(Policies.Viewer);
    }

    /// <param name="hd">true: Hauptstream statt Substream (nur, wenn die Kamera einen Substream hat).</param>
    private static async Task<IResult> Connect(
        Guid id, bool? hd, HttpContext http, LittleSisterDbContext db, Go2rtcClient go2rtc, LiveStatus status, ILogger<Go2rtcClient> logger)
    {
        if (!http.WebSockets.IsWebSocketRequest)
            return Results.BadRequest();
        if (await StreamNameAsync(id, hd, db) is not { } stream)
            return Results.NotFound();
        if (!status.Current.Available)
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: status.Current.Error);

        using var upstream = new ClientWebSocket();
        try
        {
            await upstream.ConnectAsync(go2rtc.WebSocketUri(stream), http.RequestAborted);
        }
        catch (WebSocketException ex)
        {
            logger.LogWarning(ex, "Verbindung zu go2rtc für {Stream} fehlgeschlagen", stream);
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "go2rtcFailed");
        }

        using var browser = await http.WebSockets.AcceptWebSocketAsync();
        await WebSocketRelay.RunAsync(browser, upstream, http.RequestAborted);
        return Results.Empty;
    }

    private static async Task<IResult> Snapshot(Guid id, bool? hd, HttpContext http, LittleSisterDbContext db, Go2rtcClient go2rtc, LiveStatus status)
    {
        if (await StreamNameAsync(id, hd, db) is not { } stream)
            return Results.NotFound();
        if (!status.Current.Available)
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: status.Current.Error);

        var frame = await go2rtc.GetFrameAsync(stream, http.RequestAborted);
        if (frame is null)
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "noFrame");
        http.Response.Headers.CacheControl = "no-store";
        return Results.File(frame, "image/jpeg");
    }

    /// <summary>Nur bekannte Kameras: so lassen sich über die API keine beliebigen go2rtc-Streams öffnen.</summary>
    private static async Task<string?> StreamNameAsync(Guid id, bool? hd, LittleSisterDbContext db)
    {
        var camera = await db.Cameras.AsNoTracking().Where(c => c.Id == id).Select(c => new { c.SubStreamUrl }).FirstOrDefaultAsync();
        if (camera is null)
            return null;
        return hd == true && camera.SubStreamUrl is not null ? LiveStreams.HdName(id) : LiveStreams.Name(id);
    }
}
