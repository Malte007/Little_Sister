using LittleSister.Api.Security;
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
    internal static string NormalizeAddress(string? address)
    {
        address = address?.Trim() ?? "";
        if (address.Length > 0 && !address.Contains("://", StringComparison.Ordinal))
            address = $"http://{address}/onvif/device_service";
        return address;
    }
}
