namespace LittleSister.Infrastructure.Onvif;

/// <summary>Per WS-Discovery gefundenes Gerät.</summary>
public sealed record DiscoveredDevice(string Address, string Host, string? Name, string? Hardware);

/// <summary>Grunddaten eines ONVIF-Geräts.</summary>
public sealed record OnvifDeviceInfo(string? Manufacturer, string? Model, string? FirmwareVersion, string? SerialNumber);

/// <summary>Ein Medienprofil der Kamera, z. B. Haupt- oder Substream.</summary>
public sealed record OnvifProfile(string Token, string Name, string? Encoding, int Width, int Height, string? StreamUri);

/// <summary>Ergebnis einer ONVIF-Abfrage, inklusive Vorschlag für Haupt- und Substream.</summary>
public sealed record OnvifProbeResult(
    OnvifDeviceInfo Device,
    IReadOnlyList<OnvifProfile> Profiles,
    string? SuggestedMainStream,
    string? SuggestedSubStream,
    TimeSpan ClockOffset);

/// <summary>Fehler bei der Kommunikation mit einem ONVIF-Gerät.</summary>
public class OnvifException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>Fehlercode für die Oberfläche, z. B. "onvifUnauthorized" oder "onvifUnreachable".</summary>
    public string Code { get; } = code;
}

/// <summary>Eine Ereignismeldung der Kamera, z. B. Thema "tns1:RuleEngine/CellMotionDetector/Motion" mit IsMotion=true.</summary>
/// <param name="Active">Zustand laut Meldung; null, wenn sie keinen an/aus-Wert enthält.</param>
public sealed record OnvifEvent(string Topic, string Source, bool? Active)
{
    /// <summary>Bewegungsmeldung (Bewegungserkennung oder Bewegungsalarm der Kamera)?</summary>
    public bool IsMotion => Topic.Contains("Motion", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Ein laufendes PullPoint-Abonnement; beim Freigeben wird es bei der Kamera abgemeldet.</summary>
public sealed class OnvifEventSubscription(OnvifClient client, HttpClient http, Uri address, string referenceParameters, OnvifClient.Auth? auth)
    : IAsyncDisposable
{
    /// <summary>Wie lange PullMessages auf neue Meldungen wartet.</summary>
    public static readonly TimeSpan PullTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Laufzeit des Abonnements; es wird nach der Hälfte verlängert.</summary>
    public const string Lifetime = "PT120S";
    public static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(60);

    internal HttpClient Http => http;
    internal Uri Address => address;
    internal string ReferenceParameters => referenceParameters;
    internal OnvifClient.Auth? Auth => auth;

    public Task<IReadOnlyList<OnvifEvent>> PullAsync(CancellationToken ct) => client.PullAsync(this, ct);

    public Task RenewAsync(CancellationToken ct) => client.RenewAsync(this, ct);

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.UnsubscribeAsync(this, cts.Token);
        }
        catch (Exception ex) when (ex is OnvifException or OperationCanceledException) { /* Läuft bei der Kamera ohnehin ab */ }
        http.Dispose();
    }
}
