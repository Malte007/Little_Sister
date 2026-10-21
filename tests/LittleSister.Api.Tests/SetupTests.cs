using System.Net;
using System.Net.Http.Json;

namespace LittleSister.Api.Tests;

public class SetupTests
{
    private sealed record Status(bool Required);

    [Fact]
    public async Task NeueInstallation_EinrichtungErforderlich()
    {
        using var factory = new LittleSisterFactory();
        var status = await factory.CreateClient().GetFromJsonAsync<Status>("/api/setup");
        Assert.True(status!.Required);
    }

    [Fact]
    public async Task NachEinrichtung_AngemeldetUndAssistentGesperrt()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();

        var me = await admin.GetFromJsonAsync<Me>("/api/auth/me");
        Assert.Equal(LittleSisterFactory.AdminName, me!.UserName);
        Assert.Contains("Admin", me.Roles);

        var status = await factory.CreateClient().GetFromJsonAsync<Status>("/api/setup");
        Assert.False(status!.Required);

        var second = await factory.CreateClient().PostAsJsonAsync("/api/setup", new
        {
            userName = "eindringling", password = "irgendwas-1234", language = "de",
            timeZoneId = "UTC", recordingsPath = factory.RecordingsPath,
        });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task UngueltigeEingaben_ValidationProblem()
    {
        using var factory = new LittleSisterFactory();
        var response = await factory.CreateClient().PostAsJsonAsync("/api/setup", new
        {
            userName = "admin", password = "kurz", language = "fr", timeZoneId = "UTC", recordingsPath = "relativ",
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ZuKurzesPasswort_WirdAbgelehntUndNichtsAngelegt()
    {
        using var factory = new LittleSisterFactory();
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/setup", new
        {
            userName = "admin", password = "kurz", language = "de", timeZoneId = "UTC", recordingsPath = factory.RecordingsPath,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await client.GetFromJsonAsync<Status>("/api/setup"))!.Required);
    }
}

internal sealed record Me(Guid Id, string UserName, string[] Roles);
