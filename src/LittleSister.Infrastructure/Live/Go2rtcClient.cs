using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LittleSister.Infrastructure.Live;

/// <summary>Spricht mit der lokalen go2rtc-API (nur 127.0.0.1, ohne Anmeldung).</summary>
public sealed class Go2rtcClient(HttpClient http)
{
    public Uri BaseAddress => http.BaseAddress!;

    /// <summary>WebSocket-Adresse für den Player (WebRTC-Aushandlung und MSE).</summary>
    public Uri WebSocketUri(string stream) =>
        new($"ws://{BaseAddress.Authority}/api/ws?src={Uri.EscapeDataString(stream)}");

    public async Task<bool> IsAliveAsync(CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync("api", ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>Namen und Quellen aller Streams.</summary>
    public async Task<Dictionary<string, string?>> GetStreamsAsync(CancellationToken ct)
    {
        var streams = await http.GetFromJsonAsync<Dictionary<string, JsonElement>>("api/streams", ct) ?? [];
        return streams.ToDictionary(
            s => s.Key,
            s => s.Value.TryGetProperty("producers", out var p) && p.ValueKind == JsonValueKind.Array && p.GetArrayLength() > 0
                ? p[0].GetProperty("url").GetString()
                : null);
    }

    /// <summary>Legt einen Stream an oder ändert seine Quelle. PATCH, weil PUT zusätzlich eine Konfigurationsdatei schreiben will.</summary>
    public async Task SetStreamAsync(string name, string source, CancellationToken ct)
    {
        using var response = await http.PatchAsync($"api/streams?name={Uri.EscapeDataString(name)}&src={Uri.EscapeDataString(source)}", null, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Entfernt einen Stream. go2rtc meldet 400, weil es keine Konfigurationsdatei gibt; gelöscht ist er trotzdem.</summary>
    public async Task DeleteStreamAsync(string name, CancellationToken ct)
    {
        using var response = await http.DeleteAsync($"api/streams?src={Uri.EscapeDataString(name)}", ct);
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.BadRequest))
            response.EnsureSuccessStatusCode();
    }

    /// <summary>Aktuelles Standbild als JPEG oder null, wenn die Kamera keines liefert.</summary>
    public async Task<byte[]?> GetFrameAsync(string name, CancellationToken ct)
    {
        using var response = await http.GetAsync($"api/frame.jpeg?src={Uri.EscapeDataString(name)}", ct);
        if (!response.IsSuccessStatusCode)
            return null;
        // Kurz nach dem Verbindungsaufbau antwortet go2rtc teils mit leerem Inhalt; nur echte JPEGs weitergeben
        var frame = await response.Content.ReadAsByteArrayAsync(ct);
        return frame is [0xFF, 0xD8, ..] ? frame : null;
    }
}
