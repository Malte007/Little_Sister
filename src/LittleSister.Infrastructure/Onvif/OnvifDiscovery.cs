using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace LittleSister.Infrastructure.Onvif;

/// <summary>
/// Sucht ONVIF-Kameras im lokalen Netz per WS-Discovery (UDP-Multicast 239.255.255.250:3702).
/// Im Docker-Container funktioniert das nur mit Host-Netzwerk, da Multicast nicht durch NAT geht.
/// </summary>
public sealed class OnvifDiscovery(ILogger<OnvifDiscovery> logger)
{
    private static readonly IPEndPoint MulticastEndpoint = new(IPAddress.Parse("239.255.255.250"), 3702);

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var probe = Encoding.UTF8.GetBytes(BuildProbe(Guid.NewGuid()));
        var results = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);

        // Über jede aktive IPv4-Schnittstelle senden, sonst bleiben Kameras in anderen Netzen unsichtbar
        var tasks = LocalIPv4Addresses().Select(address => ProbeOnInterfaceAsync(address, probe, timeout, ct));
        foreach (var devices in await Task.WhenAll(tasks))
        {
            foreach (var device in devices)
                results.TryAdd(device.Address, device);
        }

        return [.. results.Values.OrderBy(d => d.Host, StringComparer.Ordinal)];
    }

    private async Task<List<DiscoveredDevice>> ProbeOnInterfaceAsync(IPAddress local, byte[] probe, TimeSpan timeout, CancellationToken ct)
    {
        var found = new List<DiscoveredDevice>();
        try
        {
            using var udp = new UdpClient(new IPEndPoint(local, 0));
            await udp.SendAsync(probe, MulticastEndpoint, ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            while (!cts.IsCancellationRequested)
            {
                var response = await udp.ReceiveAsync(cts.Token);
                found.AddRange(ParseProbeMatches(Encoding.UTF8.GetString(response.Buffer)));
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Zeitlimit erreicht – normales Ende der Suche
        }
        catch (SocketException ex)
        {
            logger.LogDebug(ex, "ONVIF-Suche über {Address} nicht möglich", local);
        }
        return found;
    }

    private static IEnumerable<IPAddress> LocalIPv4Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Distinct();

    internal static string BuildProbe(Guid messageId) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope" xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing" xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery" xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
          <e:Header>
            <w:MessageID>uuid:{messageId}</w:MessageID>
            <w:To e:mustUnderstand="true">urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>
            <w:Action e:mustUnderstand="true">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action>
          </e:Header>
          <e:Body><d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe></e:Body>
        </e:Envelope>
        """;

    /// <summary>Liest Adresse, Name und Modell aus einer ProbeMatches-Antwort.</summary>
    internal static IEnumerable<DiscoveredDevice> ParseProbeMatches(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (System.Xml.XmlException) { yield break; }

        foreach (var match in doc.Descendants().Where(e => e.Name.LocalName == "ProbeMatch"))
        {
            var xaddrs = Child(match, "XAddrs")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
            // IPv4-Adresse bevorzugen; manche Kameras melden zusätzlich IPv6
            var address = xaddrs.FirstOrDefault(a => Uri.TryCreate(a, UriKind.Absolute, out var u) && u.HostNameType == UriHostNameType.IPv4)
                          ?? xaddrs.FirstOrDefault();
            if (address is null || !Uri.TryCreate(address, UriKind.Absolute, out var uri))
                continue;

            var scopes = Child(match, "Scopes")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
            yield return new DiscoveredDevice(address, uri.Host, Scope(scopes, "name"), Scope(scopes, "hardware"));
        }
    }

    private static string? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim();

    /// <summary>Liest z. B. "onvif://www.onvif.org/name/Garten%20Cam" als "Garten Cam".</summary>
    private static string? Scope(IEnumerable<string> scopes, string key)
    {
        var prefix = $"onvif://www.onvif.org/{key}/";
        var scope = scopes.FirstOrDefault(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return scope is null ? null : Uri.UnescapeDataString(scope[prefix.Length..]).Replace('_', ' ');
    }
}
