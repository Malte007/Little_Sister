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
    private sealed class FakeEventCamera : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            Requests.Add(body);
            string Answer(string inner) => string.Format(Envelope, inner);
            var xml = body switch
            {
                _ when body.Contains("GetSystemDateAndTime") => Answer("<tds:GetSystemDateAndTimeResponse xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\"/>"),
                _ when body.Contains("GetCapabilities") => Answer("""
                    <tds:GetCapabilitiesResponse xmlns:tds="http://www.onvif.org/ver10/device/wsdl" xmlns:tt="http://www.onvif.org/ver10/schema"><tds:Capabilities><tt:Events><tt:XAddr>http://192.168.1.20:8000/onvif/event_service</tt:XAddr></tt:Events></tds:Capabilities></tds:GetCapabilitiesResponse>
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
    public void Meldungen_ErkennenZustandUndQuelle()
    {
        var body = XElement.Parse($"<r>{Motion(false)}</r>");
        var e = Assert.Single(OnvifClient.ParseNotifications(body));
        Assert.Equal("tns1:RuleEngine/CellMotionDetector/Motion", e.Topic);
        Assert.Equal("VideoSourceConfigurationToken=000,Rule=MyMotion", e.Source);
        Assert.False(e.Active);
    }
}
