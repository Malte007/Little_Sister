using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LittleSister.Api.Tests;

/// <summary>Kamera-Konfigurationen exportieren und wieder importieren.</summary>
public class CameraTransferTests
{
    private static async Task<Guid> CreateAsync(HttpClient admin, string name, string? password = "geheim")
    {
        var response = await admin.PostAsJsonAsync("/api/cameras", new
        {
            name, mainStreamUrl = "rtsp://192.168.1.20/s", subStreamUrl = "rtsp://192.168.1.20/sub", username = "admin", password,
            recordingMode = "Motion", quotaGb = 50, maxRetentionDays = 14, rotation = 90, preRollSeconds = 0, postRollSeconds = 20,
            motionSensitivity = 70, motionZones = new string('1', 96) + new string('0', 96),
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Export_OhnePasswort_UndImport_LegtNeueKameraMitGleichenEinstellungenAn()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        var id = await CreateAsync(admin, "Garten");
        await CreateAsync(admin, "Einfahrt");

        var file = await admin.GetFromJsonAsync<JsonElement>($"/api/cameras/export?id={id}");
        Assert.Equal("little-sister-cameras", file.GetProperty("format").GetString());
        var exported = Assert.Single(file.GetProperty("cameras").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, exported.GetProperty("password").ValueKind);
        Assert.Equal("Motion", exported.GetProperty("recordingMode").GetString());

        var response = await admin.PostAsJsonAsync("/api/cameras/import", file);
        response.EnsureSuccessStatusCode();
        var imported = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        Assert.Equal("Garten (2)", imported.GetProperty("name").GetString());
        Assert.True(imported.GetProperty("needsPassword").GetBoolean());

        var copy = await admin.GetFromJsonAsync<JsonElement>($"/api/cameras/{imported.GetProperty("id").GetGuid()}");
        var original = await admin.GetFromJsonAsync<JsonElement>($"/api/cameras/{id}");
        foreach (var field in new[] { "mainStreamUrl", "subStreamUrl", "username", "recordingMode", "quotaGb", "maxRetentionDays", "rotation", "preRollSeconds", "postRollSeconds", "motionSensitivity", "motionZones" })
            Assert.Equal(original.GetProperty(field).ToString(), copy.GetProperty(field).ToString());
        Assert.False(copy.GetProperty("hasPassword").GetBoolean());
    }

    [Fact]
    public async Task Export_MitPasswort_ImportUebernimmtEs()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        await CreateAsync(admin, "Garten");
        await CreateAsync(admin, "Einfahrt");

        var file = await admin.GetFromJsonAsync<JsonElement>("/api/cameras/export?includePasswords=true");
        Assert.All(file.GetProperty("cameras").EnumerateArray(), c => Assert.Equal("geheim", c.GetProperty("password").GetString()));

        var imported = await (await admin.PostAsJsonAsync("/api/cameras/import", file)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["Einfahrt (2)", "Garten (2)"], imported.EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        Assert.All(imported.EnumerateArray(), c => Assert.False(c.GetProperty("needsPassword").GetBoolean()));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();
        var credentials = scope.ServiceProvider.GetRequiredService<CameraCredentialProtector>();
        var copy = await db.Cameras.SingleAsync(c => c.Name == "Garten (2)");
        Assert.Equal("geheim", credentials.Unprotect(copy.ProtectedPassword));
    }

    [Fact]
    public async Task Import_UngueltigeDatei_LegtNichtsAn()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();

        var foreign = await admin.PostAsJsonAsync("/api/cameras/import", new { format = "etwas-anderes", version = 1, exportedAtUtc = DateTimeOffset.UtcNow, cameras = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Contains("importInvalidFile", await foreign.Content.ReadAsStringAsync());

        var newer = await admin.PostAsJsonAsync("/api/cameras/import", new { format = "little-sister-cameras", version = 99, exportedAtUtc = DateTimeOffset.UtcNow, cameras = Array.Empty<object>() });
        Assert.Contains("importNewerVersion", await newer.Content.ReadAsStringAsync());

        // Zweite Kamera ungültig: auch die erste wird nicht angelegt
        var invalid = await admin.PostAsJsonAsync("/api/cameras/import", new
        {
            format = "little-sister-cameras", version = 1, exportedAtUtc = DateTimeOffset.UtcNow,
            cameras = new object[]
            {
                new { name = "Gut", mainStreamUrl = "rtsp://192.168.1.20/s", recordingEnabled = true, quotaGb = 10 },
                new { name = "", mainStreamUrl = "http://falsch", recordingEnabled = true, quotaGb = 10 },
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("cameras[1].", await invalid.Content.ReadAsStringAsync());
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>("/api/cameras")).EnumerateArray());
    }

    [Fact]
    public async Task ExportUndImport_NurFuerAdmin()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        await CreateAsync(admin, "Garten");
        var viewer = await factory.CreateUserClientAsync(admin, "betrachter", "Viewer");

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/cameras/export")).StatusCode);
        var file = await admin.GetFromJsonAsync<JsonElement>("/api/cameras/export");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/cameras/import", file)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/cameras/export?id={Guid.NewGuid()}")).StatusCode);
    }
}
