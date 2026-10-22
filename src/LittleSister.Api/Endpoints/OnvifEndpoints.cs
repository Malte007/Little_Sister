using System.Xml;
using System.Xml.Linq;
using LittleSister.Api.Security;
using LittleSister.Infrastructure.Events;
using LittleSister.Infrastructure.Onvif;

namespace LittleSister.Api.Endpoints;

/// <summary>Kamerasuche und -abfrage per ONVIF, nur für Admins.</summary>
internal static class OnvifEndpoints
{
    public static void MapOnvifEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/onvif").RequireAuthorization(Policies.Admin).WithTags("ONVIF");
        group.MapPost("/discover", Discover);
        group.MapPost("/probe", Probe);

        // Meldungen von Kameras ohne PullPoint: ohne Anmeldung, geschützt durch den zufälligen Schlüssel im Pfad
        api.MapPost("/onvif/events/{token}", Notify).AllowAnonymous().ExcludeFromDescription();
    }

    /// <summary>Größte angenommene Notify-Nachricht; echte Meldungen haben wenige Kilobyte.</summary>
    private const int MaxNotifyBytes = 256 * 1024;

    private static async Task<IResult> Notify(
        string token, HttpRequest request, OnvifEventCallback callback, ILoggerFactory loggers, CancellationToken ct)
    {
        // Zeigt bei der Fehlersuche, ob die Kamera Little Sister überhaupt erreicht
        loggers.CreateLogger("LittleSister.Api.OnvifNotify").LogInformation(
            "ONVIF-Meldung von {Remote} ({Bytes} Bytes)", request.HttpContext.Connection.RemoteIpAddress, request.ContentLength);
        if (request.ContentLength > MaxNotifyBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxNotifyBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        buffer.Position = 0;

        XDocument doc;
        try
        {
            // Keine DTDs und keine externen Verweise auswerten
            using var reader = XmlReader.Create(buffer, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            doc = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return Results.BadRequest();
        }

        return callback.Deliver(token, doc.Root!) ? Results.Ok() : Results.NotFound();
    }

    internal sealed record ProbeRequest(string Address, string? Username, string? Password);

    private static async Task<IReadOnlyList<DiscoveredDevice>> Discover(OnvifDiscovery discovery, CancellationToken ct) =>
        await discovery.DiscoverAsync(TimeSpan.FromSeconds(3), ct);

    private static async Task<IResult> Probe(ProbeRequest request, OnvifClient client, CancellationToken ct)
    {
        try
        {
            return Results.Ok(await client.ProbeAsync(NormalizeAddress(request.Address), request.Username, request.Password, ct));
        }
        catch (OnvifException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: ex.Code, detail: ex.Message);
        }
    }

    /// <summary>Erlaubt auch die Eingabe einer bloßen IP-Adresse ("192.168.1.20" oder "192.168.1.20:8000").</summary>
    internal static string NormalizeAddress(string? address) => OnvifClient.NormalizeDeviceAddress(address);
}
