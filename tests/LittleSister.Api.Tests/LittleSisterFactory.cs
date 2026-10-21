using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace LittleSister.Api.Tests;

/// <summary>Startet die API mit eigenem, temporärem Datenordner (eigene SQLite-Datei je Test).</summary>
public sealed class LittleSisterFactory : WebApplicationFactory<Program>
{
    public const string AdminName = "admin";
    public const string AdminPassword = "sehr-geheim-123";

    public string DataPath { get; } = Path.Combine(Path.GetTempPath(), "littlesister-tests", Guid.NewGuid().ToString("N"));

    public string RecordingsPath => Path.Combine(DataPath, "recordings");

    /// <summary>Zusätzliche Einstellungen je Test, z. B. kürzere Segmente.</summary>
    public Dictionary<string, string> Settings { get; } = new()
    {
        // Standardmäßig kein echtes ffmpeg: angelegte Testkameras sollen nichts aufnehmen
        ["LittleSister:Recording:FfmpegPath"] = "/nicht/vorhanden/ffmpeg",
        // Ebenso kein go2rtc; die Live-Tests schalten es gezielt ein
        ["LittleSister:Live:Go2rtcPath"] = "/nicht/vorhanden/go2rtc",
    };

    /// <summary>Dienste austauschen, z. B. die Kamera-Eingabe durch eine Testquelle.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("LittleSister:DataPath", DataPath);
        foreach (var (key, value) in Settings)
            builder.UseSetting(key, value);
        if (ConfigureServices is not null)
            builder.ConfigureTestServices(ConfigureServices);
    }

    /// <summary>Führt die Ersteinrichtung durch; der Client ist danach als Admin angemeldet.</summary>
    public async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/setup", new
        {
            userName = AdminName,
            password = AdminPassword,
            language = "de",
            timeZoneId = "Europe/Berlin",
            recordingsPath = RecordingsPath,
        });
        response.EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Legt einen Benutzer an und liefert einen per Cookie angemeldeten Client dafür.</summary>
    public async Task<HttpClient> CreateUserClientAsync(HttpClient admin, string userName, string role)
    {
        const string password = "noch-geheimer-456";
        (await admin.PostAsJsonAsync("/api/users", new { userName, password, role })).EnsureSuccessStatusCode();

        var client = CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new { userName, password })).EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Meldet sich per Bearer-Token an, z. B. für WebSockets und SignalR in Tests.</summary>
    public async Task<string> LoginTokenAsync()
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login?useTokens=true", new { userName = AdminName, password = AdminPassword });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(DataPath, recursive: true); } catch (IOException) { /* SQLite hält die Datei evtl. noch kurz offen */ }
    }
}
