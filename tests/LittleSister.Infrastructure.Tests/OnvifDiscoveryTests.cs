using LittleSister.Infrastructure.Onvif;

namespace LittleSister.Infrastructure.Tests;

public class OnvifDiscoveryTests
{
    // Gekürzte, aber echte Antwortstruktur einer Reolink-Kamera
    private const string ProbeMatches = """
        <?xml version="1.0" encoding="UTF-8"?>
        <SOAP-ENV:Envelope xmlns:SOAP-ENV="http://www.w3.org/2003/05/soap-envelope" xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing" xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery">
          <SOAP-ENV:Body>
            <d:ProbeMatches>
              <d:ProbeMatch>
                <wsa:EndpointReference><wsa:Address>urn:uuid:2419d68a-2dd2-21b2-a205-ec71dbab1234</wsa:Address></wsa:EndpointReference>
                <d:Types>dn:NetworkVideoTransmitter</d:Types>
                <d:Scopes>onvif://www.onvif.org/type/video_encoder onvif://www.onvif.org/name/Reolink%20Garten onvif://www.onvif.org/hardware/RLC-810A</d:Scopes>
                <d:XAddrs>http://[fe80::1]:8000/onvif/device_service http://192.168.1.20:8000/onvif/device_service</d:XAddrs>
              </d:ProbeMatch>
            </d:ProbeMatches>
          </SOAP-ENV:Body>
        </SOAP-ENV:Envelope>
        """;

    [Fact]
    public void ParseProbeMatches_LiestAdresseNameUndModell()
    {
        var device = Assert.Single(OnvifDiscovery.ParseProbeMatches(ProbeMatches));
        Assert.Equal("http://192.168.1.20:8000/onvif/device_service", device.Address);
        Assert.Equal("192.168.1.20", device.Host);
        Assert.Equal("Reolink Garten", device.Name);
        Assert.Equal("RLC-810A", device.Hardware);
    }

    [Fact]
    public void ParseProbeMatches_UngueltigesXml_LiefertNichts()
    {
        Assert.Empty(OnvifDiscovery.ParseProbeMatches("kein xml"));
    }

    [Fact]
    public void Probe_IstGueltigesXmlMitNachrichtenId()
    {
        var id = Guid.NewGuid();
        var probe = System.Xml.Linq.XDocument.Parse(OnvifDiscovery.BuildProbe(id));
        Assert.Contains(id.ToString(), probe.ToString());
    }
}
