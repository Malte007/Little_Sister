using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace LittleSister.Api.Tests;

public class AuthTests
{
    private sealed record TokenResponse(string AccessToken, string RefreshToken);

    [Fact]
    public async Task OhneAnmeldung_Unauthorized()
    {
        using var factory = new LittleSisterFactory();
        await factory.CreateAdminClientAsync();

        var response = await factory.CreateClient().GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FalschesPasswort_Unauthorized()
    {
        using var factory = new LittleSisterFactory();
        await factory.CreateAdminClientAsync();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { userName = LittleSisterFactory.AdminName, password = "falsch-falsch" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NachFuenfFehlversuchen_KontoGesperrt()
    {
        using var factory = new LittleSisterFactory();
        await factory.CreateAdminClientAsync();
        var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/auth/login", new { userName = LittleSisterFactory.AdminName, password = "falsch-falsch" });

        // Selbst das richtige Passwort wird jetzt abgelehnt
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = LittleSisterFactory.AdminName, password = LittleSisterFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("lockedOut", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ZuVieleVersuche_WerdenGedrosselt()
    {
        using var factory = new LittleSisterFactory();
        var client = factory.CreateClient();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 11; i++)
            last = await client.PostAsJsonAsync("/api/auth/login", new { userName = "x", password = "y" });

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }

    [Fact]
    public async Task TokenLogin_FuerApps_FunktioniertMitBearerUndRefresh()
    {
        using var factory = new LittleSisterFactory();
        await factory.CreateAdminClientAsync();
        var client = factory.CreateClient(new() { HandleCookies = false });

        var login = await client.PostAsJsonAsync("/api/auth/login?useTokens=true",
            new { userName = LittleSisterFactory.AdminName, password = LittleSisterFactory.AdminPassword });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<TokenResponse>();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);

        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    }

    [Fact]
    public async Task Logout_BeendetSitzung()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();

        (await admin.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/auth/me")).StatusCode);
    }
}
