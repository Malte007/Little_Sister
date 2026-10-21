using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LittleSister.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LittleSister.Api.Tests;

public class CameraTests
{
    private static object NewCamera(string name = "Garten", string? password = "kamera-pw") => new
    {
        name,
        mainStreamUrl = "rtsp://192.168.1.20:554/h264Preview_01_main",
        subStreamUrl = "rtsp://192.168.1.20:554/h264Preview_01_sub",
        username = "admin",
        password,
        recordingEnabled = true,
        quotaGb = 200,
        maxRetentionDays = 14,
    };

    [Fact]
    public async Task Admin_LegtKameraAn_PasswortWirdNieAusgeliefert()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();

        var created = await admin.PostAsJsonAsync("/api/cameras", NewCamera());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var json = await created.Content.ReadAsStringAsync();
        Assert.DoesNotContain("kamera-pw", json);
        Assert.Contains("\"hasPassword\":true", json);

        var list = await admin.GetStringAsync("/api/cameras");
        Assert.Contains("Garten", list);
        Assert.DoesNotContain("kamera-pw", list);
    }

    [Fact]
    public async Task Drehung_WirdGespeichert_UngueltigeWirdAbgelehnt()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();

        var created = await admin.PostAsJsonAsync("/api/cameras", new { name = "Flur", mainStreamUrl = "rtsp://cam/1", quotaGb = 1, rotation = 90 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(90, (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rotation").GetInt32());

        var invalid = await admin.PostAsJsonAsync("/api/cameras", new { name = "Flur", mainStreamUrl = "rtsp://cam/1", quotaGb = 1, rotation = 45 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("invalidRotation", await invalid.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Passwort_LiegtVerschluesseltInDerDatenbank()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        await admin.PostAsJsonAsync("/api/cameras", NewCamera());

        using var scope = factory.Services.CreateScope();
        var camera = await scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>().Cameras.SingleAsync();
        Assert.NotNull(camera.ProtectedPassword);
        Assert.NotEqual("kamera-pw", camera.ProtectedPassword);
    }

    [Fact]
    public async Task ZugangsdatenInDerUrl_WerdenAbgetrennt()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/cameras", new
        {
            name = "Einfahrt", mainStreamUrl = "rtsp://admin:p%40ss@192.168.1.21/stream", recordingEnabled = false, quotaGb = 50,
        });
        var camera = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rtsp://192.168.1.21/stream", camera.GetProperty("mainStreamUrl").GetString());
        Assert.Equal("admin", camera.GetProperty("username").GetString());
        Assert.True(camera.GetProperty("hasPassword").GetBoolean());
    }

    [Fact]
    public async Task Aendern_OhnePasswort_BehaeltAltesPasswort()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        var created = await (await admin.PostAsJsonAsync("/api/cameras", NewCamera())).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var updated = await admin.PutAsJsonAsync($"/api/cameras/{id}", NewCamera("Garten hinten", password: null));
        updated.EnsureSuccessStatusCode();
        var camera = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Garten hinten", camera.GetProperty("name").GetString());
        Assert.True(camera.GetProperty("hasPassword").GetBoolean());
    }

    [Fact]
    public async Task UngueltigeDaten_ValidationProblem()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/cameras", new { name = "", mainStreamUrl = "http://x", quotaGb = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("rtspUrlRequired", body);
    }

    [Fact]
    public async Task Betrachter_DarfAnsehenAberNichtAnlegen()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        var viewer = await factory.CreateUserClientAsync(admin, "oma", "Viewer");

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/cameras")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/cameras", NewCamera())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/onvif/discover", null)).StatusCode);
    }

    [Fact]
    public async Task Loeschen_EntferntKamera()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        var created = await (await admin.PostAsJsonAsync("/api/cameras", NewCamera())).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/cameras/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/cameras/{id}")).StatusCode);
    }

    [Fact]
    public async Task OnvifProbe_UnerreichbaresGeraet_422()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();

        // Port 9 (discard) auf localhost ist praktisch nie offen
        var response = await admin.PostAsJsonAsync("/api/onvif/probe", new { address = "127.0.0.1:9" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("onvifUnreachable", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Speicher_ZeigtPfadUndGrenze()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();

        (await admin.PutAsJsonAsync("/api/storage", new { minFreeGb = 25 })).EnsureSuccessStatusCode();
        var storage = await admin.GetFromJsonAsync<JsonElement>("/api/storage");
        Assert.Equal(factory.RecordingsPath, storage.GetProperty("recordingsPath").GetString());
        Assert.Equal(25, storage.GetProperty("minFreeGb").GetDouble());
        Assert.True(storage.GetProperty("freeBytes").GetInt64() > 0);
    }

    [Fact]
    public async Task Aufnahmeordner_LaesstSichAendern()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        var newPath = Path.Combine(factory.DataPath, "neue-platte");

        (await admin.PutAsJsonAsync("/api/storage", new { minFreeGb = 10, recordingsPath = newPath })).EnsureSuccessStatusCode();

        var storage = await admin.GetFromJsonAsync<JsonElement>("/api/storage");
        Assert.Equal(newPath, storage.GetProperty("recordingsPath").GetString());
        Assert.True(Directory.Exists(newPath));
    }

    [Fact]
    public async Task Aufnahmeordner_RelativerPfad_WirdAbgelehnt()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        var viewer = await factory.CreateUserClientAsync(admin, "oma", "Viewer");

        var response = await admin.PutAsJsonAsync("/api/storage", new { minFreeGb = 10, recordingsPath = "relativ/pfad" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("absolutePathRequired", await response.Content.ReadAsStringAsync());

        var forbidden = await viewer.PutAsJsonAsync("/api/storage", new { minFreeGb = 10, recordingsPath = "/tmp" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
