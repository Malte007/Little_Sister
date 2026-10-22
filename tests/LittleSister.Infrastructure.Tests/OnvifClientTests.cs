using System.Net;
using System.Text;
using LittleSister.Infrastructure.Onvif;

namespace LittleSister.Infrastructure.Tests;

public class OnvifClientTests
{
    private const string Envelope = """<s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"><s:Body>{0}</s:Body></s:Envelope>""";

    /// <summary>Simuliert eine Kamera: Antwort je nach aufgerufener SOAP-Methode.</summary>
    private sealed class FakeCamera(bool rejectLogin = false) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            Requests.Add(body);

            string Answer(string inner) => string.Format(Envelope, inner);
            var xml = body switch
            {
                _ when body.Contains("GetSystemDateAndTime") => Answer("""
                    <tds:GetSystemDateAndTimeResponse xmlns:tds="http://www.onvif.org/ver10/device/wsdl" xmlns:tt="http://www.onvif.org/ver10/schema"><tds:SystemDateAndTime>
                    <tt:UTCDateTime><tt:Time><tt:Hour>12</tt:Hour><tt:Minute>5</tt:Minute><tt:Second>0</tt:Second></tt:Time><tt:Date><tt:Year>2026</tt:Year><tt:Month>10</tt:Month><tt:Day>21</tt:Day></tt:Date></tt:UTCDateTime>
                    </tds:SystemDateAndTime></tds:GetSystemDateAndTimeResponse>
                    """),
                _ when rejectLogin => Answer("""
                    <s:Fault><s:Code><s:Value>s:Sender</s:Value><s:Subcode><s:Value>ter:NotAuthorized</s:Value></s:Subcode></s:Code><s:Reason><s:Text xml:lang="en">Sender not Authorized</s:Text></s:Reason></s:Fault>
                    """),
                _ when body.Contains("GetDeviceInformation") => Answer("""
                    <tds:GetDeviceInformationResponse xmlns:tds="http://www.onvif.org/ver10/device/wsdl"><tds:Manufacturer>Reolink</tds:Manufacturer><tds:Model>RLC-810A</tds:Model><tds:FirmwareVersion>v3.1</tds:FirmwareVersion><tds:SerialNumber>123</tds:SerialNumber></tds:GetDeviceInformationResponse>
                    """),
                _ when body.Contains("GetCapabilities") => Answer("""
                    <tds:GetCapabilitiesResponse xmlns:tds="http://www.onvif.org/ver10/device/wsdl" xmlns:tt="http://www.onvif.org/ver10/schema"><tds:Capabilities><tt:Media><tt:XAddr>http://192.168.1.20:8000/onvif/media_service</tt:XAddr></tt:Media></tds:Capabilities></tds:GetCapabilitiesResponse>
                    """),
                _ when body.Contains("GetProfiles") => Answer("""
                    <trt:GetProfilesResponse xmlns:trt="http://www.onvif.org/ver10/media/wsdl" xmlns:tt="http://www.onvif.org/ver10/schema">
                      <trt:Profiles token="000"><tt:Name>mainStream</tt:Name><tt:VideoEncoderConfiguration token="v0"><tt:Name>v0</tt:Name><tt:Encoding>H264</tt:Encoding><tt:Resolution><tt:Width>3840</tt:Width><tt:Height>2160</tt:Height></tt:Resolution></tt:VideoEncoderConfiguration></trt:Profiles>
                      <trt:Profiles token="001"><tt:Name>subStream</tt:Name><tt:VideoEncoderConfiguration token="v1"><tt:Name>v1</tt:Name><tt:Encoding>H264</tt:Encoding><tt:Resolution><tt:Width>640</tt:Width><tt:Height>360</tt:Height></tt:Resolution></tt:VideoEncoderConfiguration></trt:Profiles>
                    </trt:GetProfilesResponse>
                    """),
                _ when body.Contains("GetStreamUri") => Answer($"""
                    <trt:GetStreamUriResponse xmlns:trt="http://www.onvif.org/ver10/media/wsdl" xmlns:tt="http://www.onvif.org/ver10/schema"><trt:MediaUri><tt:Uri>rtsp://192.168.1.20:554/{(body.Contains(">000<") ? "h264Preview_01_main" : "h264Preview_01_sub")}</tt:Uri></trt:MediaUri></trt:GetStreamUriResponse>
                    """),
                _ => throw new InvalidOperationException("Unerwartete Anfrage"),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/soap+xml") };
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset ServerNow = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Probe_LiefertGeraetProfileUndVorschlaege()
    {
        var camera = new FakeCamera();
        var client = new OnvifClient(_ => camera, new FixedTime(ServerNow));

        var result = await client.ProbeAsync("http://192.168.1.20:8000/onvif/device_service", "admin", "geheim");

        Assert.Equal("Reolink", result.Device.Manufacturer);
        Assert.Equal("RLC-810A", result.Device.Model);
        Assert.Equal(2, result.Profiles.Count);
        Assert.Equal("rtsp://192.168.1.20:554/h264Preview_01_main", result.SuggestedMainStream);
        Assert.Equal("rtsp://192.168.1.20:554/h264Preview_01_sub", result.SuggestedSubStream);
        Assert.Equal(TimeSpan.FromMinutes(5), result.ClockOffset);
    }

    [Fact]
    public async Task Probe_SendetWsSecurityMitKamerazeit()
    {
        var camera = new FakeCamera();
        var client = new OnvifClient(_ => camera, new FixedTime(ServerNow));

        await client.ProbeAsync("http://192.168.1.20:8000/onvif/device_service", "admin", "geheim");

        var authenticated = camera.Requests.First(r => r.Contains("GetDeviceInformation"));
        Assert.Contains("<Username>admin</Username>", authenticated);
        Assert.Contains("PasswordDigest", authenticated);
        Assert.DoesNotContain("geheim", authenticated); // Passwort nie im Klartext
        Assert.Contains("2026-10-21T12:05:00", authenticated); // Uhrzeit der Kamera, nicht des Servers
    }

    [Fact]
    public async Task Probe_FalscheZugangsdaten_Unauthorized()
    {
        var client = new OnvifClient(_ => new FakeCamera(rejectLogin: true), new FixedTime(ServerNow));
        var ex = await Assert.ThrowsAsync<OnvifException>(() => client.ProbeAsync("http://192.168.1.20/onvif/device_service", "admin", "falsch"));
        Assert.Equal("onvifUnauthorized", ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://192.168.1.20")]
    public async Task Probe_UngueltigeAdresse(string address)
    {
        var client = new OnvifClient(_ => new FakeCamera(), TimeProvider.System);
        var ex = await Assert.ThrowsAsync<OnvifException>(() => client.ProbeAsync(address, null, null));
        Assert.Equal("onvifInvalidAddress", ex.Code);
    }

    [Theory]
    [InlineData("192.168.0.160", "http://192.168.0.160/onvif/device_service")]
    [InlineData("192.168.0.160:8080", "http://192.168.0.160:8080/onvif/device_service")]
    [InlineData(" http://192.168.0.160:8080 ", "http://192.168.0.160:8080/onvif/device_service")]
    [InlineData("http://192.168.0.160:8080/", "http://192.168.0.160:8080/onvif/device_service")]
    [InlineData("http://192.168.0.160:8080/onvif/device_service", "http://192.168.0.160:8080/onvif/device_service")]
    [InlineData("http://192.168.0.160/onvif/service", "http://192.168.0.160/onvif/service")]
    [InlineData("", "")]
    public void Adresse_wird_vervollstaendigt(string input, string expected) =>
        Assert.Equal(expected, OnvifClient.NormalizeDeviceAddress(input));
}
