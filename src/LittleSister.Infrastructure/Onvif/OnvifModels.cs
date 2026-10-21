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
