using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using LittleSister.Core.Cameras;

namespace LittleSister.Infrastructure.Onvif;

/// <summary>
/// Schlanker ONVIF-Client (SOAP 1.2): Geräteinfo, Profile und RTSP-Adressen für die Einrichtung,
/// außerdem Ereignisse (Bewegung) per PullPoint-Abonnement.
/// Authentifiziert per WS-Security UsernameToken (Digest); HTTP-Digest übernimmt der HttpClientHandler.
/// </summary>
public sealed class OnvifClient(Func<NetworkCredential?, HttpMessageHandler> handlerFactory, TimeProvider time)
{
    private static readonly XNamespace Soap = "http://www.w3.org/2003/05/soap-envelope";
    private const string DeviceNs = "http://www.onvif.org/ver10/device/wsdl";
    private const string MediaNs = "http://www.onvif.org/ver10/media/wsdl";
    private const string SchemaNs = "http://www.onvif.org/ver10/schema";
    private const string EventsNs = "http://www.onvif.org/ver10/events/wsdl";
    private const string NotificationNs = "http://docs.oasis-open.org/wsn/b-2";
    private const string AddressingNs = "http://www.w3.org/2005/08/addressing";

    public OnvifClient(TimeProvider time)
        : this(credential => new HttpClientHandler { Credentials = credential, PreAuthenticate = false }, time) { }

    /// <summary>Fragt Gerät und Medienprofile ab und schlägt Haupt- und Substream vor.</summary>
    public async Task<OnvifProbeResult> ProbeAsync(string deviceAddress, string? username, string? password, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(deviceAddress, UriKind.Absolute, out var deviceUri) || deviceUri.Scheme is not ("http" or "https"))
            throw new OnvifException("onvifInvalidAddress", "Ungültige ONVIF-Adresse.");

        var credential = string.IsNullOrEmpty(username) ? null : new NetworkCredential(username, password);
        using var http = new HttpClient(handlerFactory(credential)) { Timeout = TimeSpan.FromSeconds(8) };

        // Die Kamera prüft den Zeitstempel im Login; weicht ihre Uhr ab, rechnen wir mit ihrer Zeit
        var offset = await GetClockOffsetAsync(http, deviceUri, ct);
        var auth = credential is null ? null : new Auth(username!, password ?? "", offset);

        var info = await SendAsync(http, deviceUri, $"<GetDeviceInformation xmlns=\"{DeviceNs}\"/>", auth, ct);
        var device = new OnvifDeviceInfo(
            Value(info, "Manufacturer"), Value(info, "Model"), Value(info, "FirmwareVersion"), Value(info, "SerialNumber"));

        var caps = await SendAsync(http, deviceUri, $"<GetCapabilities xmlns=\"{DeviceNs}\"><Category>Media</Category></GetCapabilities>", auth, ct);
        var mediaAddress = caps.Descendants().FirstOrDefault(e => e.Name.LocalName == "Media")?
            .Elements().FirstOrDefault(e => e.Name.LocalName == "XAddr")?.Value.Trim();
        var mediaUri = Uri.TryCreate(mediaAddress, UriKind.Absolute, out var m) ? m : deviceUri;

        var profilesXml = await SendAsync(http, mediaUri, $"<GetProfiles xmlns=\"{MediaNs}\"/>", auth, ct);
        var profiles = new List<OnvifProfile>();
        foreach (var profile in profilesXml.Descendants().Where(e => e.Name.LocalName == "Profiles"))
        {
            var token = profile.Attribute("token")?.Value;
            if (token is null)
                continue;
            var encoder = profile.Elements().FirstOrDefault(e => e.Name.LocalName == "VideoEncoderConfiguration");
            if (encoder is null)
                continue; // Reine Audio- oder Metadatenprofile

            var resolution = encoder.Elements().FirstOrDefault(e => e.Name.LocalName == "Resolution");
            var streamUri = await GetStreamUriAsync(http, mediaUri, token, auth, ct);
            profiles.Add(new OnvifProfile(
                token,
                Value(profile, "Name") ?? token,
                Value(encoder, "Encoding"),
                ParseInt(resolution, "Width"),
                ParseInt(resolution, "Height"),
                streamUri is null ? null : StreamUrl.WithoutCredentials(streamUri, out _, out _)));
        }

        // Größte Auflösung für die Aufnahme, kleinste für Live-Kacheln
        var ordered = profiles.Where(p => p.StreamUri is not null).OrderByDescending(p => p.Width * p.Height).ToList();
        var main = ordered.FirstOrDefault()?.StreamUri;
        var sub = ordered.Count > 1 ? ordered[^1].StreamUri : null;
        return new OnvifProbeResult(device, profiles, main, sub == main ? null : sub, offset);
    }

    /// <summary>
    /// Meldet sich für Ereignisse der Kamera an (PullPoint). Abgeholt wird mit
    /// <see cref="OnvifEventSubscription.PullAsync"/>; das Abonnement muss regelmäßig verlängert werden.
    /// </summary>
    public async Task<OnvifEventSubscription> SubscribeEventsAsync(string deviceAddress, string? username, string? password, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(deviceAddress, UriKind.Absolute, out var deviceUri) || deviceUri.Scheme is not ("http" or "https"))
            throw new OnvifException("onvifInvalidAddress", "Ungültige ONVIF-Adresse.");

        var credential = string.IsNullOrEmpty(username) ? null : new NetworkCredential(username, password);
        // Abholen wartet bis zu PullTimeout auf neue Meldungen; die Verbindung braucht etwas Luft darüber
        var http = new HttpClient(handlerFactory(credential)) { Timeout = OnvifEventSubscription.PullTimeout + TimeSpan.FromSeconds(20) };
        try
        {
            var offset = await GetClockOffsetAsync(http, deviceUri, ct);
            var auth = credential is null ? null : new Auth(username!, password ?? "", offset);

            var eventsUri = await FindEventsAddressAsync(http, deviceUri, auth, ct);

            var body = $"<CreatePullPointSubscription xmlns=\"{EventsNs}\"><InitialTerminationTime>{OnvifEventSubscription.Lifetime}</InitialTerminationTime></CreatePullPointSubscription>";
            var response = await SendAsync(http, eventsUri, body, auth, ct,
                Addressing($"{EventsNs}/EventPortType/CreatePullPointSubscriptionRequest", eventsUri.ToString()));
            var reference = response.Descendants().FirstOrDefault(e => e.Name.LocalName == "SubscriptionReference")
                ?? throw new OnvifException("onvifInvalidResponse", "Antwort ohne Abonnement.");
            var address = reference.Elements().FirstOrDefault(e => e.Name.LocalName == "Address")?.Value.Trim();
            if (!Uri.TryCreate(address, UriKind.Absolute, out var subscriptionUri))
                throw new OnvifException("onvifInvalidResponse", "Abonnement ohne Adresse.");

            // Manche Kameras erwarten ihre Referenzparameter in jeder weiteren Anfrage zurück
            var parameters = reference.Elements().FirstOrDefault(e => e.Name.LocalName == "ReferenceParameters")?.Elements()
                .Select(p =>
                {
                    p.SetAttributeValue(XName.Get("IsReferenceParameter", AddressingNs), "true");
                    return p.ToString(SaveOptions.DisableFormatting);
                })
                .ToList() ?? [];

            return new OnvifEventSubscription(this, http, subscriptionUri, string.Concat(parameters), auth);
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Sucht die Adresse des Ereignisdienstes. Kameras antworten hier sehr unterschiedlich: manche nur auf
    /// GetCapabilities mit "All", manche nur auf GetServices; manche melden eine unbrauchbare IP.
    /// </summary>
    private async Task<Uri> FindEventsAddressAsync(HttpClient http, Uri deviceUri, Auth? auth, CancellationToken ct)
    {
        var tried = new List<string>();
        foreach (var (name, body) in new[]
        {
            ("GetCapabilities(Events)", $"<GetCapabilities xmlns=\"{DeviceNs}\"><Category>Events</Category></GetCapabilities>"),
            ("GetCapabilities(All)", $"<GetCapabilities xmlns=\"{DeviceNs}\"><Category>All</Category></GetCapabilities>"),
            ("GetServices", $"<GetServices xmlns=\"{DeviceNs}\"><IncludeCapability>false</IncludeCapability></GetServices>"),
        })
        {
            try
            {
                var response = await SendAsync(http, deviceUri, body, auth, ct);
                if (ParseEventsAddress(response, deviceUri) is { } uri)
                    return uri;
                tried.Add($"{name}: keine Adresse");
            }
            catch (OnvifException ex) when (ex.Code == "onvifFault")
            {
                // Nicht unterstützte Abfrage: nächste Variante probieren
                tried.Add($"{name}: {ex.Message}");
            }
        }
        throw new OnvifException("onvifNoEvents", $"Die Kamera nennt keinen Ereignisdienst ({string.Join("; ", tried)}).");
    }

    /// <summary>Liest die Adresse des Ereignisdienstes aus GetCapabilities- oder GetServices-Antworten.</summary>
    internal static Uri? ParseEventsAddress(XElement body, Uri deviceUri)
    {
        var candidates = body.Descendants().Where(e => e.Name.LocalName == "Events")
            .Select(e => e.Elements().FirstOrDefault(x => x.Name.LocalName == "XAddr")?.Value)
            .Concat(body.Descendants().Where(e => e.Name.LocalName == "Service"
                    && e.Elements().Any(x => x.Name.LocalName == "Namespace" && x.Value.Trim() == EventsNs))
                .Select(e => e.Elements().FirstOrDefault(x => x.Name.LocalName == "XAddr")?.Value));

        foreach (var candidate in candidates)
        {
            // Manche Kameras nennen mehrere Adressen durch Leerzeichen getrennt (z. B. IPv4 und IPv6)
            foreach (var text in (candidate ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                    continue;
                // Unbrauchbare Hosts (0.0.0.0, localhost) durch die Adresse ersetzen, unter der die Kamera antwortet
                if (uri.Host is "0.0.0.0" or "localhost" || uri.Host.StartsWith("127.", StringComparison.Ordinal))
                    uri = new UriBuilder(uri) { Host = deviceUri.Host }.Uri;
                return uri;
            }
        }
        return null;
    }

    /// <summary>Holt neue Ereignisse ab; wartet höchstens <see cref="OnvifEventSubscription.PullTimeout"/>.</summary>
    internal async Task<IReadOnlyList<OnvifEvent>> PullAsync(OnvifEventSubscription s, CancellationToken ct)
    {
        var timeout = System.Xml.XmlConvert.ToString(OnvifEventSubscription.PullTimeout);
        var response = await SendAsync(s.Http, s.Address,
            $"<PullMessages xmlns=\"{EventsNs}\"><Timeout>{timeout}</Timeout><MessageLimit>64</MessageLimit></PullMessages>",
            s.Auth, ct, Addressing($"{EventsNs}/PullPointSubscription/PullMessagesRequest", s.Address.ToString(), s.ReferenceParameters));
        return ParseNotifications(response);
    }

    internal Task RenewAsync(OnvifEventSubscription s, CancellationToken ct) =>
        SendAsync(s.Http, s.Address,
            $"<Renew xmlns=\"{NotificationNs}\"><TerminationTime>{OnvifEventSubscription.Lifetime}</TerminationTime></Renew>",
            s.Auth, ct, Addressing("http://docs.oasis-open.org/wsn/bw-2/SubscriptionManager/RenewRequest", s.Address.ToString(), s.ReferenceParameters));

    internal Task UnsubscribeAsync(OnvifEventSubscription s, CancellationToken ct) =>
        SendAsync(s.Http, s.Address, $"<Unsubscribe xmlns=\"{NotificationNs}\"/>",
            s.Auth, ct, Addressing("http://docs.oasis-open.org/wsn/bw-2/SubscriptionManager/UnsubscribeRequest", s.Address.ToString(), s.ReferenceParameters));

    /// <summary>Liest die Meldungen aus einer PullMessages-Antwort: Thema, Quelle und Zustand (z. B. IsMotion).</summary>
    internal static IReadOnlyList<OnvifEvent> ParseNotifications(XElement body)
    {
        var result = new List<OnvifEvent>();
        foreach (var notification in body.Descendants().Where(e => e.Name.LocalName == "NotificationMessage"))
        {
            var topic = notification.Elements().FirstOrDefault(e => e.Name.LocalName == "Topic")?.Value.Trim() ?? "";
            var message = notification.Descendants().FirstOrDefault(e => e.Name.LocalName == "Message" && e.Attribute("UtcTime") is not null);
            Dictionary<string, string> Items(string section) =>
                message?.Elements().FirstOrDefault(e => e.Name.LocalName == section)?
                    .Elements().Where(e => e.Name.LocalName == "SimpleItem")
                    .GroupBy(e => e.Attribute("Name")?.Value ?? "")
                    .ToDictionary(g => g.Key, g => g.First().Attribute("Value")?.Value ?? "") ?? [];

            var source = string.Join(",", Items("Source").Select(kv => $"{kv.Key}={kv.Value}"));
            // Zustand: IsMotion (Bewegungserkennung), State (Alarm), sonst der erste Wert
            var data = Items("Data");
            var value = data.TryGetValue("IsMotion", out var m) ? m : data.TryGetValue("State", out var st) ? st : data.Values.FirstOrDefault();
            bool? active = value?.Trim().ToLowerInvariant() switch
            {
                "true" or "1" => true,
                "false" or "0" => false,
                _ => null,
            };
            result.Add(new OnvifEvent(topic, source, active));
        }
        return result;
    }

    /// <summary>WS-Addressing-Kopf, den PullPoint-Abonnements verlangen.</summary>
    private static string Addressing(string action, string to, string referenceParameters = "") =>
        $"<wsa:Action xmlns:wsa=\"{AddressingNs}\">{action}</wsa:Action>"
        + $"<wsa:To xmlns:wsa=\"{AddressingNs}\">{System.Security.SecurityElement.Escape(to)}</wsa:To>{referenceParameters}";

    private async Task<string?> GetStreamUriAsync(HttpClient http, Uri mediaUri, string token, Auth? auth, CancellationToken ct)
    {
        var body = $"""
            <GetStreamUri xmlns="{MediaNs}">
              <StreamSetup>
                <Stream xmlns="{SchemaNs}">RTP-Unicast</Stream>
                <Transport xmlns="{SchemaNs}"><Protocol>RTSP</Protocol></Transport>
              </StreamSetup>
              <ProfileToken>{System.Security.SecurityElement.Escape(token)}</ProfileToken>
            </GetStreamUri>
            """;
        var response = await SendAsync(http, mediaUri, body, auth, ct);
        return response.Descendants().FirstOrDefault(e => e.Name.LocalName == "Uri")?.Value.Trim();
    }

    private async Task<TimeSpan> GetClockOffsetAsync(HttpClient http, Uri deviceUri, CancellationToken ct)
    {
        try
        {
            var response = await SendAsync(http, deviceUri, $"<GetSystemDateAndTime xmlns=\"{DeviceNs}\"/>", null, ct);
            var utc = response.Descendants().FirstOrDefault(e => e.Name.LocalName == "UTCDateTime");
            if (utc is null)
                return TimeSpan.Zero;

            var date = utc.Elements().First(e => e.Name.LocalName == "Date");
            var timeOfDay = utc.Elements().First(e => e.Name.LocalName == "Time");
            var cameraTime = new DateTimeOffset(
                ParseInt(date, "Year"), ParseInt(date, "Month"), ParseInt(date, "Day"),
                ParseInt(timeOfDay, "Hour"), ParseInt(timeOfDay, "Minute"), ParseInt(timeOfDay, "Second"), TimeSpan.Zero);
            return cameraTime - time.GetUtcNow();
        }
        catch (OnvifException ex) when (ex.Code != "onvifUnreachable")
        {
            return TimeSpan.Zero; // Nicht jede Kamera liefert die Zeit ohne Login
        }
    }

    private async Task<XElement> SendAsync(HttpClient http, Uri uri, string body, Auth? auth, CancellationToken ct, string extraHeader = "")
    {
        var envelope = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <s:Envelope xmlns:s="{Soap}"><s:Header>{extraHeader}{auth?.ToSecurityHeader(time)}</s:Header><s:Body>{body}</s:Body></s:Envelope>
            """;

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(uri, new StringContent(envelope, Encoding.UTF8, "application/soap+xml"), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new OnvifException("onvifUnreachable", $"ONVIF-Gerät {uri.Host} nicht erreichbar.", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            XDocument doc;
            try { doc = XDocument.Parse(text); }
            catch (System.Xml.XmlException ex)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new OnvifException("onvifUnauthorized", "Zugangsdaten abgelehnt.", ex);
                throw new OnvifException("onvifInvalidResponse", $"Ungültige Antwort ({(int)response.StatusCode}).", ex);
            }

            var fault = doc.Descendants(Soap + "Fault").FirstOrDefault();
            if (fault is not null || response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var reason = string.Join(" ", fault?.Descendants().Where(e => e.Name.LocalName is "Value" or "Text").Select(e => e.Value) ?? []);
                var unauthorized = response.StatusCode == HttpStatusCode.Unauthorized
                    || reason.Contains("NotAuthorized", StringComparison.OrdinalIgnoreCase)
                    || reason.Contains("FailedAuthentication", StringComparison.OrdinalIgnoreCase)
                    || reason.Contains("Sender not Authorized", StringComparison.OrdinalIgnoreCase);
                throw new OnvifException(unauthorized ? "onvifUnauthorized" : "onvifFault", $"ONVIF-Fehler: {reason}");
            }

            return doc.Descendants(Soap + "Body").First();
        }
    }

    private static string? Value(XElement parent, string localName) =>
        parent.Descendants().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim();

    private static int ParseInt(XElement? parent, string localName) =>
        int.TryParse(parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    /// <summary>WS-Security UsernameToken mit Passwort-Digest = Base64(SHA1(Nonce + Created + Passwort)).</summary>
    public sealed record Auth(string Username, string Password, TimeSpan ClockOffset)
    {
        public string ToSecurityHeader(TimeProvider time)
        {
            var nonce = RandomNumberGenerator.GetBytes(16);
            var created = (time.GetUtcNow() + ClockOffset).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            var digest = Convert.ToBase64String(SHA1.HashData([.. nonce, .. Encoding.UTF8.GetBytes(created), .. Encoding.UTF8.GetBytes(Password)]));

            return $"""
                <Security s:mustUnderstand="1" xmlns="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd">
                  <UsernameToken>
                    <Username>{System.Security.SecurityElement.Escape(Username)}</Username>
                    <Password Type="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest">{digest}</Password>
                    <Nonce EncodingType="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary">{Convert.ToBase64String(nonce)}</Nonce>
                    <Created xmlns="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd">{created}</Created>
                  </UsernameToken>
                </Security>
                """;
        }
    }
}
