using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using System.Xml.Linq;
using LittleSister.Infrastructure.Onvif;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

namespace LittleSister.Infrastructure.Events;

/// <summary>
/// Nimmt Meldungen von Kameras entgegen, die kein PullPoint können und ihre Ereignisse selbst schicken
/// (WS-BaseNotification). Jedes Abonnement bekommt eine eigene Adresse mit zufälligem Schlüssel.
/// </summary>
public sealed class OnvifEventCallback(IServer server, IOptions<LittleSisterOptions> options) : IOnvifEventCallback
{
    /// <summary>Pfad der Rückrufadresse; {token} ist der Schlüssel des Abonnements.</summary>
    public const string PathPrefix = "/api/onvif/events/";

    private readonly ConcurrentDictionary<string, Channel<IReadOnlyList<OnvifEvent>>> receivers = new();

    public (string Token, ChannelReader<IReadOnlyList<OnvifEvent>> Events) Register()
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        // Hängt die Auswertung, gehen die ältesten Meldungen verloren statt den Speicher zu füllen
        var channel = Channel.CreateBounded<IReadOnlyList<OnvifEvent>>(
            new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        receivers[token] = channel;
        return (token, channel.Reader);
    }

    public void Unregister(string token)
    {
        if (receivers.TryRemove(token, out var channel))
            channel.Writer.TryComplete();
    }

    /// <summary>Übergibt eine Notify-Nachricht der Kamera.</summary>
    /// <returns>false, wenn der Schlüssel unbekannt ist.</returns>
    public bool Deliver(string token, XElement body)
    {
        if (!receivers.TryGetValue(token, out var channel))
            return false;
        channel.Writer.TryWrite(OnvifClient.ParseNotifications(body));
        return true;
    }

    public Uri AddressFor(string cameraHost, string token)
    {
        if (!string.IsNullOrWhiteSpace(options.Value.Events.CallbackUrl))
            return new Uri(new Uri(options.Value.Events.CallbackUrl.TrimEnd('/') + "/"), PathPrefix.TrimStart('/') + token);

        // Port, auf dem Little Sister per HTTP lauscht
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var listen = addresses
            .Select(a => Uri.TryCreate(a.Replace("://+", "://0.0.0.0").Replace("://*", "://0.0.0.0"), UriKind.Absolute, out var u) ? u : null)
            .FirstOrDefault(u => u?.Scheme == "http")
            ?? throw new OnvifException("eventsCallbackUnavailable", "Little Sister lauscht auf keiner HTTP-Adresse.");
        if (listen.IsLoopback)
            throw new OnvifException("eventsCallbackLocalhost",
                $"Little Sister lauscht nur auf {listen.Authority}; die Kamera kann ihre Meldungen so nicht zustellen.");

        // Eigene IP aus Sicht der Kamera: die Adresse, über die das System die Kamera erreicht (UDP verschickt dabei nichts)
        var target = IPAddress.TryParse(cameraHost.Trim('[', ']'), out var ip) ? ip : Dns.GetHostAddresses(cameraHost)[0];
        using var socket = new Socket(target.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.Connect(target, 80);
        var local = ((IPEndPoint)socket.LocalEndPoint!).Address;
        var host = local.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{local}]" : local.ToString();
        return new Uri($"http://{host}:{listen.Port}{PathPrefix}{token}");
    }
}
