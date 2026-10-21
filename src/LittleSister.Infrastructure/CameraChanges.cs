using System.Threading.Channels;

namespace LittleSister.Infrastructure;

/// <summary>
/// Meldet geänderte Kameras oder Einstellungen an alle Hintergrunddienste (Aufnahme, Live-Ansicht),
/// damit sie ihren Stand sofort neu abgleichen statt auf den nächsten Takt zu warten.
/// </summary>
public sealed class CameraChanges
{
    private readonly Lock gate = new();
    private readonly List<Channel<bool>> subscribers = [];

    /// <summary>Liefert einen eigenen Kanal; mehrere Meldungen hintereinander werden zu einer zusammengefasst.</summary>
    public ChannelReader<bool> Subscribe()
    {
        var channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        lock (gate)
            subscribers.Add(channel);
        return channel.Reader;
    }

    public void Notify()
    {
        lock (gate)
            foreach (var channel in subscribers)
                channel.Writer.TryWrite(true);
    }

    /// <summary>Wartet auf die nächste Meldung, höchstens aber <paramref name="timeout"/>.</summary>
    public static async Task WaitAsync(ChannelReader<bool> reader, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }
}
