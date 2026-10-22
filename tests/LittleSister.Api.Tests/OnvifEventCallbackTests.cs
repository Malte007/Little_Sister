using System.Net;
using System.Text;
using LittleSister.Infrastructure.Events;
using LittleSister.Infrastructure.Onvif;
using Microsoft.Extensions.DependencyInjection;

namespace LittleSister.Api.Tests;

/// <summary>Kameras ohne PullPoint schicken ihre Meldungen per HTTP an Little Sister.</summary>
public class OnvifEventCallbackTests
{
    private const string Notify = """
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"><s:Body>
          <wsnt:Notify xmlns:wsnt="http://docs.oasis-open.org/wsn/b-2" xmlns:tt="http://www.onvif.org/ver10/schema">
            <wsnt:NotificationMessage><wsnt:Topic>tns1:VideoSource/MotionAlarm</wsnt:Topic>
              <wsnt:Message><tt:Message UtcTime="2026-10-22T16:00:00Z"><tt:Source><tt:SimpleItem Name="Source" Value="0"/></tt:Source>
              <tt:Data><tt:SimpleItem Name="State" Value="true"/></tt:Data></tt:Message></wsnt:Message>
            </wsnt:NotificationMessage>
          </wsnt:Notify>
        </s:Body></s:Envelope>
        """;

    [Fact]
    public async Task Meldung_KommtOhneAnmeldungAn_UnbekannterSchluesselNicht()
    {
        using var factory = new LittleSisterFactory();
        var client = factory.CreateClient();
        var callback = factory.Services.GetRequiredService<OnvifEventCallback>();
        var (token, events) = callback.Register();

        var response = await client.PostAsync($"/api/onvif/events/{token}", new StringContent(Notify, Encoding.UTF8, "application/soap+xml"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(events.TryRead(out var batch));
        var e = Assert.Single(batch);
        Assert.True(e.IsMotion);
        Assert.True(e.Active);

        var unknown = await client.PostAsync("/api/onvif/events/falsch", new StringContent(Notify, Encoding.UTF8, "application/soap+xml"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // Nach dem Abmelden gilt der Schlüssel nicht mehr
        callback.Unregister(token);
        var gone = await client.PostAsync($"/api/onvif/events/{token}", new StringContent(Notify, Encoding.UTF8, "application/soap+xml"));
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Meldung_MitDtd_WirdAbgelehnt()
    {
        using var factory = new LittleSisterFactory();
        var (token, _) = factory.Services.GetRequiredService<OnvifEventCallback>().Register();
        var xml = """<?xml version="1.0"?><!DOCTYPE x [<!ENTITY a "b">]><x>&a;</x>""";
        var response = await factory.CreateClient().PostAsync($"/api/onvif/events/{token}", new StringContent(xml, Encoding.UTF8, "application/xml"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Rueckrufadresse_AusEinstellung()
    {
        using var factory = new LittleSisterFactory();
        factory.Settings["LittleSister:Events:CallbackUrl"] = "http://192.168.1.10:8080/";
        var address = factory.Services.GetRequiredService<OnvifEventCallback>().AddressFor("192.168.1.20", "abc");
        Assert.Equal(new Uri("http://192.168.1.10:8080/api/onvif/events/abc"), address);
    }

    [Fact]
    public void Rueckrufadresse_OhneHttpAdresse_Fehler()
    {
        using var factory = new LittleSisterFactory();
        var ex = Assert.Throws<OnvifException>(() => factory.Services.GetRequiredService<OnvifEventCallback>().AddressFor("192.168.1.20", "abc"));
        Assert.Equal("eventsCallbackUnavailable", ex.Code);
    }
}
