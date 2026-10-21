using System.Net;
using System.Net.Http.Json;

namespace LittleSister.Api.Tests;

public class UserTests
{
    private sealed record UserDto(Guid Id, string UserName, string Role);

    [Fact]
    public async Task Admin_KannBetrachterAnlegenUndAuflisten()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        await factory.CreateUserClientAsync(admin, "oma", "viewer");

        var users = await admin.GetFromJsonAsync<UserDto[]>("/api/users");
        Assert.Contains(users!, u => u.UserName == "oma" && u.Role == "Viewer");
    }

    [Fact]
    public async Task Betrachter_DarfKeineBenutzerVerwalten()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        var viewer = await factory.CreateUserClientAsync(admin, "oma", "Viewer");

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/users")).StatusCode);
        var create = await viewer.PostAsJsonAsync("/api/users", new { userName = "x", password = "abcdefghijk", role = "Admin" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        // Lesende Endpunkte bleiben erlaubt
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/system/info")).StatusCode);
    }

    [Fact]
    public async Task UnbekannteRolle_WirdAbgelehnt()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/users", new { userName = "x", password = "abcdefghijk", role = "Gott" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_KannSichNichtSelbstLoeschen()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        var me = await admin.GetFromJsonAsync<Me>("/api/auth/me");

        var response = await admin.DeleteAsync($"/api/users/{me!.Id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
