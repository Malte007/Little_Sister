using System.Runtime.CompilerServices;
using LittleSister.Core.Cameras;
using LittleSister.Infrastructure.Onvif;
using LittleSister.Infrastructure.Security;

namespace LittleSister.Infrastructure.Events;

/// <summary>Liefert den Bewegungszustand einer Kamera; austauschbar für Tests.</summary>
public interface IMotionSource
{
    /// <summary>Zuerst den aktuellen Zustand, danach jede Änderung. Bricht die Verbindung ab, gibt es eine Ausnahme.</summary>
    IAsyncEnumerable<bool> WatchAsync(Camera camera, CancellationToken ct);
}

/// <summary>Bewegung aus den ONVIF-Ereignissen der Kamera (PullPoint-Abonnement).</summary>
public sealed class OnvifMotionSource(OnvifClient onvif, CameraCredentialProtector credentials, TimeProvider time) : IMotionSource
{
    public async IAsyncEnumerable<bool> WatchAsync(Camera camera, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var subscription = await onvif.SubscribeEventsAsync(
            camera.OnvifAddress!, camera.Username, credentials.Unprotect(camera.ProtectedPassword), ct);

        // Zustand je Thema und Quelle; Bewegung, solange irgendeine davon aktiv ist
        var states = new Dictionary<string, bool>();
        bool? last = null;
        var renewAt = time.GetUtcNow() + OnvifEventSubscription.RenewInterval;

        while (!ct.IsCancellationRequested)
        {
            var started = time.GetUtcNow();
            var events = await subscription.PullAsync(ct);
            foreach (var e in events.Where(e => e.IsMotion && e.Active is not null))
                states[$"{e.Topic}|{e.Source}"] = e.Active!.Value;

            var active = states.Values.Any(v => v);
            if (active != last)
            {
                last = active;
                yield return active;
            }

            if (time.GetUtcNow() >= renewAt)
            {
                await subscription.RenewAsync(ct);
                renewAt = time.GetUtcNow() + OnvifEventSubscription.RenewInterval;
            }

            // Manche Kameras warten nicht auf neue Meldungen, sondern antworten sofort: dann nicht im Kreis fragen
            if (events.Count == 0 && time.GetUtcNow() - started < TimeSpan.FromSeconds(1))
                await Task.Delay(TimeSpan.FromSeconds(1), time, ct);
        }
    }
}
