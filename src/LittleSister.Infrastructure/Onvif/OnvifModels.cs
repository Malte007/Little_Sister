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

/// <summary>
/// Ein laufendes Ereignis-Abonnement; beim Freigeben wird es bei der Kamera abgemeldet.
/// Ohne <paramref name="pushed"/> fragt Little Sister die Kamera ab (PullPoint), sonst schickt die Kamera
/// ihre Meldungen selbst und <paramref name="address"/> ist nur noch zum Verlängern und Abmelden da.
/// </summary>
public sealed class OnvifEventSubscription(
    OnvifClient client, HttpClient http, Uri address, string referenceParameters, OnvifClient.Auth? auth,
    System.Threading.Channels.ChannelReader<IReadOnlyList<OnvifEvent>>? pushed = null, Action? onDispose = null, Uri? consumer = null)
    : IAsyncDisposable
{
    /// <summary>Adresse, an die die Kamera ihre Meldungen schickt (nur ohne PullPoint).</summary>
    public Uri? Consumer => consumer;

    /// <summary>Warum PullPoint nicht ging, falls ersatzweise die Kamera selbst schickt.</summary>
    public string? PullPointError { get; init; }

    /// <summary>Wie lange PullMessages auf neue Meldungen wartet.</summary>
    public static readonly TimeSpan PullTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Laufzeit des Abonnements; es wird nach der Hälfte verlängert.</summary>
    public const string Lifetime = "PT120S";
    public static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(60);

    internal HttpClient Http => http;
    internal Uri Address => address;
    internal string ReferenceParameters => referenceParameters;
    internal OnvifClient.Auth? Auth => auth;

    /// <summary>Kamera schickt ihre Meldungen selbst (WS-BaseNotification)?</summary>
    public bool IsPush => pushed is not null;

    /// <summary>Neue Meldungen; wartet höchstens <see cref="PullTimeout"/>.</summary>
    public Task<IReadOnlyList<OnvifEvent>> PullAsync(CancellationToken ct) =>
        pushed is null ? client.PullAsync(this, ct) : ReadPushedAsync(pushed, ct);

    private static async Task<IReadOnlyList<OnvifEvent>> ReadPushedAsync(
        System.Threading.Channels.ChannelReader<IReadOnlyList<OnvifEvent>> reader, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PullTimeout);
        try
        {
            await reader.WaitToReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return [];
        }
        var result = new List<OnvifEvent>();
        while (reader.TryRead(out var batch))
            result.AddRange(batch);
        return result;
    }

    public Task RenewAsync(CancellationToken ct) => client.RenewAsync(this, ct);

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.UnsubscribeAsync(this, cts.Token);
        }
        catch (Exception ex) when (ex is OnvifException or OperationCanceledException) { /* Läuft bei der Kamera ohnehin ab */ }
        onDispose?.Invoke();
        http.Dispose();
    }
}

/// <summary>Empfang für Meldungen, die Kameras per WS-BaseNotification an Little Sister schicken.</summary>
public interface IOnvifEventCallback
{
    /// <summary>Neue Empfangsstelle mit zufälligem Schlüssel; die Meldungen kommen über den Kanal.</summary>
    (string Token, System.Threading.Channels.ChannelReader<IReadOnlyList<OnvifEvent>> Events) Register();

    void Unregister(string token);

    /// <summary>Adresse, unter der die Kamera Little Sister erreicht.</summary>
    Uri AddressFor(string cameraHost, string token);
}
