using System.Buffers;
using System.Net.WebSockets;

namespace LittleSister.Api.Live;

/// <summary>Reicht Nachrichten zwischen Browser und go2rtc in beide Richtungen unverändert durch.</summary>
internal static class WebSocketRelay
{
    public static async Task RunAsync(WebSocket browser, WebSocket upstream, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var toUpstream = PumpAsync(browser, upstream, cts.Token);
        var toBrowser = PumpAsync(upstream, browser, cts.Token);

        // Schließt eine Seite, wird auch die andere beendet
        await Task.WhenAny(toUpstream, toBrowser);
        await cts.CancelAsync();
        await Task.WhenAll(Quiet(toUpstream), Quiet(toBrowser));
        await CloseAsync(browser);
        await CloseAsync(upstream);
    }

    private static async Task PumpAsync(WebSocket from, WebSocket to, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (from.State == WebSocketState.Open && to.State == WebSocketState.Open)
            {
                var result = await from.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                await to.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, result.EndOfMessage, ct);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task Quiet(Task task)
    {
        try { await task; }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { /* Verbindung beendet */ }
    }

    private static async Task CloseAsync(WebSocket socket)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
            return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
    }
}
