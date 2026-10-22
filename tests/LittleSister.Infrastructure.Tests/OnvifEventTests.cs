using System.Net;
using System.Text;
using System.Xml.Linq;
using LittleSister.Infrastructure.Onvif;

namespace LittleSister.Infrastructure.Tests;

public class OnvifEventTests
{
    private const string Envelope = """<s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"><s:Body>{0}</s:Body></s:Envelope>""";

    // Bewegungsmeldung, wie sie z. B. Reolink und Hikvision schicken
    private static string Motion(bool active) => $"""
        <wsnt:NotificationMessage xmlns:wsnt="http://docs.oasis-open.org/wsn/b-2" xmlns:tt="http://www.onvif.org/ver10/schema">
          <wsnt:Topic Dialect="http://www.onvif.org/ver10/tev/topicExpression/ConcreteSet">tns1:RuleEngine/CellMotionDetector/Motion</wsnt:Topic>
          <wsnt:Message><tt:Message UtcTime="2026-10-21T12:00:00Z" PropertyOperation="Changed">
            <tt:Source><tt:SimpleItem Name="VideoSourceConfigurationToken" Value="000"/><tt:SimpleItem Name="Rule" Value="MyMotion"/></tt:Source>
            <tt:Data><tt:SimpleItem Name="IsMotion" Value="{(active ? "true" : "false")}"/></tt:Data>
          </tt:Message></wsnt:Message>
        </wsnt:NotificationMessage>
        """;

    /// <summary>Kamera mit PullPoint-Ereignissen; liefert beim ersten Abholen eine Bewegung.</summary>
    /// <param name="onlyServices">Kamera kennt nur GetServices; GetCapabilities liefert einen Fehler.</param>
    /// <param name="noPullPoint">Kamera kann kein PullPoint, nur Subscribe (sie schickt ihre Meldungen selbst).</param>
    private sealed class FakeEventCamera(bool onlyServices = false, bool noPullPoint = false) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public List<string?> ContentTypes { get; } = [];

        private const string NotSupported = """
            <s:Fault><s:Code><s:Value>s:Receiver</s:Value><s:Subcode><s:Value>ter:ActionNotSupported</s:Value></s:Subcode></s:Code><s:Reason><s:Text xml:lang="en">Action Not Implemented</s:Text></s:Reason></s:Fault>
            """;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            Requests.Add(body);
            ContentTypes.Add(request.Content.Headers.ContentType?.ToString());
            string Answer(string inner) => string.Format(Envelope, inner);
            var xml = body switch
            {
                _ when body.Contains("GetSystemDateAndTime") => Answer("<tds:GetSystemDateAndTimeResponse xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\"/>"),
                _ when onlyServices && body.Contains("GetCapabilities") => Answer("""
                    <s:Fault><s:Code><s:Value>s:Receiver</s:Value><s:Subcode><s:Value>ter:ActionNotSupported</s:Value></s:Subcode></s:Code><s:Reason><s:Text xml:lang="en">Not supported</s:Text></s:Reason></s:Fault>
                    """),
                _ when body.Contains("GetServices") => Answer("""
                    <tds:GetServicesResponse xmlns:tds="http://www.onvif.org/ver10/device/wsdl">
                      <tds:Service><tds:Namespace>http://www.onvif.org/ver10/media/wsdl</tds:Namespace><tds:XAddr>http://192.168.1.20:8000/onvif/media</tds:XAddr></tds:Service>
                      <tds:Service><tds:Namespace>http://www.onvif.org/ver10/events/wsdl</tds:Namespace><tds:XAddr>http://192.168.1.20:8000/onvif/event_service</tds:XAddr></tds:Service>
                    </tds:GetServicesResponse>
                    """),
                _ when body.Contains("GetCapabilities") => Answer("""
                    <tds:GetCapabilitiesResponse xmlns:tds="http://www.onvif.org/ver10/device/wsdl" xmlns:tt="http://www.onvif.org/ver10/schema"><tds:Capabilities><tt:Events><tt:XAddr>http://192.168.1.20:8000/onvif/event_service</tt:XAddr></tt:Events></tds:Capabilities></tds:GetCapabilitiesResponse>
                    """),
                _ when noPullPoint && body.Contains("CreatePullPointSubscription") => Answer(NotSupported),
                _ when body.Contains("<Subscribe ") => Answer("""
                    <wsnt:SubscribeResponse xmlns:wsnt="http://docs.oasis-open.org/wsn/b-2" xmlns:wsa="http://www.w3.org/2005/08/addressing">
                      <wsnt:SubscriptionReference><wsa:Address>http://192.168.1.20:8000/onvif/subscription?id=9</wsa:Address></wsnt:SubscriptionReference>
                    </wsnt:SubscribeResponse>
                    """),
                _ when body.Contains("CreatePullPointSubscription") => Answer("""
                    <tev:CreatePullPointSubscriptionResponse xmlns:tev="http://www.onvif.org/ver10/events/wsdl" xmlns:wsa="http://www.w3.org/2005/08/addressing">
                      <tev:SubscriptionReference><wsa:Address>http://192.168.1.20:8000/onvif/pullpoint?id=7</wsa:Address><wsa:ReferenceParameters><dom0:SubscriptionId xmlns:dom0="http://www.example.com">7</dom0:SubscriptionId></wsa:ReferenceParameters></tev:SubscriptionReference>
                    </tev:CreatePullPointSubscriptionResponse>
                    """),
                _ when body.Contains("PullMessages") => Answer($"<tev:PullMessagesResponse xmlns:tev=\"http://www.onvif.org/ver10/events/wsdl\">{Motion(true)}</tev:PullMessagesResponse>"),
                _ when body.Contains("Unsubscribe") => Answer("<wsnt:UnsubscribeResponse xmlns:wsnt=\"http://docs.oasis-open.org/wsn/b-2\"/>"),
                _ => throw new InvalidOperationException("Unerwartete Anfrage"),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/soap+xml") };
        }
    }

    [Fact]
    public async Task Abonnement_HoltBewegungAbUndMeldetSichAb()
    {
        var camera = new FakeEventCamera();
        var client = new OnvifClient(_ => camera, TimeProvider.System);

        await using (var subscription = await client.SubscribeEventsAsync("http://192.168.1.20:8000/onvif/device_service", "admin", "geheim"))
        {
            var events = await subscription.PullAsync(CancellationToken.None);
            var motion = Assert.Single(events);
            Assert.True(motion.IsMotion);
            Assert.True(motion.Active);
        }

        var pull = camera.Requests.Single(r => r.Contains("PullMessages"));
        Assert.Contains("http://192.168.1.20:8000/onvif/pullpoint?id=7", pull); // an die Abonnement-Adresse
        Assert.Contains("IsReferenceParameter", pull); // Referenzparameter zurückgeschickt
        Assert.Contains(camera.Requests, r => r.Contains("Unsubscribe"));
    }

    [Fact]
    public async Task Abonnement_FindetEreignisdienstUeberGetServices()
    {
        var camera = new FakeEventCamera(onlyServices: true);
        var client = new OnvifClient(_ => camera, TimeProvider.System);

        await using var subscription = await client.SubscribeEventsAsync("http://192.168.1.20:8000/onvif/device_service", "admin", "geheim");

        Assert.Contains(camera.Requests, r => r.Contains("GetServices"));
        Assert.Contains(camera.Requests, r => r.Contains("CreatePullPointSubscription"));
    }

    /// <summary>Rückruf-Empfang für Tests: feste Adresse, Meldungen schiebt der Test selbst hinein.</summary>
    private sealed class FakeCallback : IOnvifEventCallback
    {
        public System.Threading.Channels.Channel<IReadOnlyList<OnvifEvent>> Channel { get; } =
            System.Threading.Channels.Channel.CreateUnbounded<IReadOnlyList<OnvifEvent>>();
        public string? Unregistered { get; private set; }

        public (string Token, System.Threading.Channels.ChannelReader<IReadOnlyList<OnvifEvent>> Events) Register() => ("abc", Channel.Reader);
        public void Unregister(string token) => Unregistered = token;
        public Uri AddressFor(string cameraHost, string token) => new($"http://192.168.1.10:8080/api/onvif/events/{token}");
    }

    [Fact]
    public async Task OhnePullPoint_MeldetSichMitRueckrufadresseAn()
    {
        var camera = new FakeEventCamera(noPullPoint: true);
        var callback = new FakeCallback();
        var client = new OnvifClient(_ => camera, TimeProvider.System);

        await using (var subscription = await client.SubscribeEventsAsync(
            "http://192.168.1.20:8000/onvif/device_service", "admin", "geheim", CancellationToken.None, callback))
        {
            Assert.True(subscription.IsPush);
            var subscribe = camera.Requests.Single(r => r.Contains("<Subscribe "));
            Assert.Contains("http://192.168.1.10:8080/api/onvif/events/abc", subscribe);

            // Von der Kamera geschickte Meldungen kommen beim Abholen an
            callback.Channel.Writer.TryWrite(OnvifClient.ParseNotifications(XElement.Parse($"<r>{Motion(true)}</r>")));
            var events = await subscription.PullAsync(CancellationToken.None);
            Assert.True(Assert.Single(events).Active);
        }

        // Abmelden an der Adresse des Abonnements, Empfang wieder freigegeben
        Assert.Contains(camera.Requests, r => r.Contains("Unsubscribe") && r.Contains("subscription?id=9"));
        Assert.Equal("abc", callback.Unregistered);
        // Aktion auch im Content-Type, manche Kameras ordnen Anfragen nur darüber zu
        Assert.Contains(camera.ContentTypes, c => c!.Contains("action=\"http://docs.oasis-open.org/wsn/bw-2/NotificationProducer/SubscribeRequest\""));
    }

    [Fact]
    public async Task OhnePullPointUndOhneRueckruf_FehlerBleibt()
    {
        var client = new OnvifClient(_ => new FakeEventCamera(noPullPoint: true), TimeProvider.System);
        var ex = await Assert.ThrowsAsync<OnvifException>(() =>
            client.SubscribeEventsAsync("http://192.168.1.20:8000/onvif/device_service", "admin", "geheim"));
        Assert.Equal("onvifFault", ex.Code);
    }

    [Theory]
    [InlineData("http://192.168.1.20/onvif/events", "http://192.168.1.20/onvif/events")]
    [InlineData("http://0.0.0.0:8000/onvif/events", "http://192.168.1.20:8000/onvif/events")]
    [InlineData("http://[fe80::1]/onvif/events http://192.168.1.20/ev", "http://[fe80::1]/onvif/events")]
    [InlineData("kaputt http://192.168.1.20/ev", "http://192.168.1.20/ev")]
    public void EreignisAdresse_WirdBereinigt(string xaddr, string expected)
    {
        var body = XElement.Parse($"<r><Capabilities><Events><XAddr>{xaddr}</XAddr></Events></Capabilities></r>");
        Assert.Equal(new Uri(expected), OnvifClient.ParseEventsAddress(body, new Uri("http://192.168.1.20/onvif/device_service")));
    }

    [Fact]
    public void Meldungen_ErkennenZustandUndQuelle()
    {
        var body = XElement.Parse($"<r>{Motion(false)}</r>");
        var e = Assert.Single(OnvifClient.ParseNotifications(body));
        Assert.Equal("tns1:RuleEngine/CellMotionDetector/Motion", e.Topic);
        Assert.Equal("VideoSourceConfigurationToken=000,Rule=MyMotion", e.Source);
        Assert.False(e.Active);
    }
}
