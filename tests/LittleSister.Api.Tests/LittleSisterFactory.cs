using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LittleSister.Api.Tests;

/// <summary>Startet die API mit eigenem, temporärem Datenordner (eigene SQLite-Datei je Test).</summary>
public sealed class LittleSisterFactory : WebApplicationFactory<Program>
{
    public const string AdminName = "admin";
    public const string AdminPassword = "sehr-geheim-123";

    public string DataPath { get; } = Path.Combine(Path.GetTempPath(), "littlesister-tests", Guid.NewGuid().ToString("N"));

    public string RecordingsPath => Path.Combine(DataPath, "recordings");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("LittleSister:DataPath", DataPath);
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(DataPath, recursive: true); } catch (IOException) { /* SQLite hält die Datei evtl. noch kurz offen */ }
    }
}
