using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LittleSister.Infrastructure;
using LittleSister.Infrastructure.Live;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace LittleSister.Api.Tests;

/// <summary>Live-Ansicht über go2rtc und Statusmeldungen per SignalR.</summary>
public class LiveTests
{
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>Ein zweites go2rtc spielt eine Kamera: Testbild per ffmpeg als RTSP-Stream.</summary>
    private sealed class FakeCamera : IDisposable
    {
        private readonly Process process;

        public FakeCamera()
        {
            RtspPort = FreePort();
            var config = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["api"] = new { listen = "" },
                ["rtsp"] = new { listen = $"127.0.0.1:{RtspPort}" },
                ["webrtc"] = new { listen = "" },
                ["srtp"] = new { listen = "" },
                ["streams"] = new Dictionary<string, string>
                {
                    ["cam"] = "exec:ffmpeg -hide_banner -loglevel error -re -f lavfi -i testsrc=size=320x240:rate=10 " +
                              "-c:v libx264 -preset ultrafast -tune zerolatency -g 10 -pix_fmt yuv420p -f rtsp {output}",
                },
            });
            var psi = new ProcessStartInfo("go2rtc") { RedirectStandardOutput = true };
            psi.ArgumentList.Add("-config");
            psi.ArgumentList.Add(config);
            process = Process.Start(psi)!;
            _ = process.StandardOutput.ReadToEndAsync();
        }

        public int RtspPort { get; }

        public string Url => $"rtsp://127.0.0.1:{RtspPort}/cam";

        public void Dispose()
        {
            process.Kill(entireProcessTree: true);
            process.Dispose();
        }
    }

    private static LittleSisterFactory CreateFactoryWithGo2rtc()
    {
        var factory = new LittleSisterFactory();
        factory.Settings["LittleSister:Recording:FfmpegPath"] = "ffmpeg";
        factory.Settings["LittleSister:Live:Go2rtcPath"] = "go2rtc";
        factory.Settings["LittleSister:Live:ApiPort"] = FreePort().ToString();
        factory.Settings["LittleSister:Live:WebRtcPort"] = FreePort().ToString();
        return factory;
    }

    private static async Task<Guid> CreateCameraAsync(HttpClient admin, string url)
    {
        var created = await admin.PostAsJsonAsync("/api/cameras", new { name = "Testbild", mainStreamUrl = url, recordingEnabled = false, quotaGb = 1 });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Wiederholt eine Prüfung bis zu <paramref name="seconds"/> Sekunden.</summary>
    private static async Task<T> EventuallyAsync<T>(Func<Task<T>> get, Func<T, bool> ok, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        T value;
        while (!ok(value = await get()) && DateTime.UtcNow < deadline)
            await Task.Delay(250);
        return value;
    }

    [Fact]
    public async Task OhneGo2rtc_IstLiveNichtVerfuegbar_AufnahmeApiLaeuftWeiter()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        var id = await CreateCameraAsync(admin, "rtsp://127.0.0.1:9/nichts");

        var state = await EventuallyAsync(
            () => admin.GetFromJsonAsync<JsonElement>("/api/live"),
            s => s.GetProperty("error").GetString() == "go2rtcMissing");
        Assert.False(state.GetProperty("available").GetBoolean());
        Assert.Equal("go2rtcMissing", state.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await admin.GetAsync($"/api/live/{id}/snapshot")).StatusCode);
    }

    [Fact]
    public async Task Live_NurAngemeldetUndNurFuerBekannteKameras()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        var viewer = await factory.CreateUserClientAsync(admin, "betrachter", "Viewer");
        var id = await CreateCameraAsync(admin, "rtsp://127.0.0.1:9/nichts");

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/live")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/live/{id}/snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/live")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/live/{Guid.NewGuid()}/snapshot")).StatusCode);
    }

    [Fact]
    public async Task MitGo2rtc_LiefertStandbildUndVideoUeberWebSocket()
    {
        using var camera = new FakeCamera();
        using var factory = CreateFactoryWithGo2rtc();
        var admin = await factory.CreateAdminClientAsync();
        var id = await CreateCameraAsync(admin, camera.Url);

        var live = await EventuallyAsync(() => admin.GetFromJsonAsync<JsonElement>("/api/live"), s => s.GetProperty("available").GetBoolean());
        Assert.True(live.GetProperty("available").GetBoolean(), live.ToString());

        // Standbild: go2rtc holt einen Keyframe von der Kamera und wandelt ihn in JPEG
        var snapshot = await EventuallyAsync(() => admin.GetAsync($"/api/live/{id}/snapshot"), r => r.IsSuccessStatusCode, 30);
        Assert.Equal(HttpStatusCode.OK, snapshot.StatusCode);
        Assert.Equal("image/jpeg", snapshot.Content.Headers.ContentType?.MediaType);
        var jpeg = await snapshot.Content.ReadAsByteArrayAsync();
        Assert.True(jpeg is [0xFF, 0xD8, ..], "Kein JPEG");

        // Player-Verbindung wie im Browser: MSE anfragen, Codec-Antwort und Videodaten erwarten
        var token = await factory.LoginTokenAsync();
        var ws = factory.Server.CreateWebSocketClient();
        ws.ConfigureRequest = r => r.Headers.Authorization = $"Bearer {token}";
        using var socket = await ws.ConnectAsync(new Uri(factory.Server.BaseAddress, $"/api/live/{id}/ws"), CancellationToken.None);
        await socket.SendAsync(Encoding.UTF8.GetBytes("""{"type":"mse","value":"avc1.640029,avc1.64002A,avc1.640033,mp4a.40.2"}"""),
            WebSocketMessageType.Text, true, CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var buffer = new byte[256 * 1024];
        var text = await socket.ReceiveAsync(buffer, timeout.Token);
        Assert.Equal(WebSocketMessageType.Text, text.MessageType);
        var answer = JsonDocument.Parse(buffer.AsMemory(0, text.Count)).RootElement;
        Assert.Equal("mse", answer.GetProperty("type").GetString());
        Assert.Contains("avc1", answer.GetProperty("value").GetString());

        var data = await socket.ReceiveAsync(buffer, timeout.Token);
        Assert.Equal(WebSocketMessageType.Binary, data.MessageType);
        Assert.True(data.Count > 0);
    }

    [Fact]
    public async Task MitGo2rtc_GeloeschteKameraVerschwindetAusGo2rtc()
    {
        using var factory = CreateFactoryWithGo2rtc();
        var admin = await factory.CreateAdminClientAsync();
        var id = await CreateCameraAsync(admin, "rtsp://127.0.0.1:9/nichts");
        var go2rtc = (Go2rtcClient)factory.Services.GetService(typeof(Go2rtcClient))!;

        var streams = await EventuallyAsync(() => SafeStreamsAsync(go2rtc), s => s.ContainsKey(LiveStreams.Name(id)));
        Assert.Contains(LiveStreams.Name(id), streams.Keys);
        Assert.EndsWith("#backchannel=0", streams[LiveStreams.Name(id)]);

        (await admin.DeleteAsync($"/api/cameras/{id}")).EnsureSuccessStatusCode();
        streams = await EventuallyAsync(() => SafeStreamsAsync(go2rtc), s => !s.ContainsKey(LiveStreams.Name(id)));
        Assert.DoesNotContain(LiveStreams.Name(id), streams.Keys);
    }

    private static async Task<Dictionary<string, string?>> SafeStreamsAsync(Go2rtcClient go2rtc)
    {
        try { return await go2rtc.GetStreamsAsync(CancellationToken.None); }
        catch (HttpRequestException) { return []; }
    }

    [Fact]
    public async Task Statusaenderungen_KommenPerSignalR()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        var token = await factory.LoginTokenAsync();

        var received = new TaskCompletionSource<(Guid Id, JsonElement Status)>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/api/hubs/status"), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        hub.On<Guid, JsonElement>("status", (id, status) => received.TrySetResult((id, status)));
        await hub.StartAsync();

        // Neue Kamera ohne Aufnahme: der Supervisor meldet sie als "aus"
        var cameraId = await CreateCameraAsync(admin, "rtsp://127.0.0.1:9/nichts");
        var (receivedId, receivedStatus) = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(cameraId, receivedId);
        Assert.Equal("Idle", receivedStatus.GetProperty("state").GetString());
    }

    [Fact]
    public void Konfiguration_HaeltGo2rtcApiLokalUndOhneFremdeServer()
    {
        var config = JsonDocument.Parse(LiveSupervisor.BuildConfig(new LiveOptions { ApiPort = 1984, WebRtcPort = 8555 }, "ffmpeg")).RootElement;
        Assert.Equal("127.0.0.1:1984", config.GetProperty("api").GetProperty("listen").GetString());
        Assert.Equal("", config.GetProperty("rtsp").GetProperty("listen").GetString());
        Assert.Equal(0, config.GetProperty("webrtc").GetProperty("ice_servers").GetArrayLength());
    }
}
