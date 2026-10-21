using System.Collections.Concurrent;

namespace LittleSister.Infrastructure.Recording;

/// <summary>Zustand einer Kamera aus Sicht der Aufnahme.</summary>
public enum CameraState
{
    /// <summary>Aufnahme ist für diese Kamera ausgeschaltet.</summary>
    Idle,
    /// <summary>ffmpeg startet und verbindet sich.</summary>
    Connecting,
    /// <summary>Segmente werden geschrieben.</summary>
    Recording,
    /// <summary>Verbindung verloren oder ffmpeg abgestürzt; Neustart läuft.</summary>
    Error,
}

public sealed record CameraStatus(
    CameraState State,
    DateTimeOffset SinceUtc,
    string? LastError = null,
    int Restarts = 0,
    DateTimeOffset? LastSegmentUtc = null);

/// <summary>Aktueller Zustand aller Kameras im Speicher (wird nicht persistiert).</summary>
public sealed class CameraStatusRegistry(TimeProvider time)
{
    private readonly ConcurrentDictionary<Guid, CameraStatus> statuses = new();

    public CameraStatus Get(Guid cameraId) =>
        statuses.TryGetValue(cameraId, out var status) ? status : new CameraStatus(CameraState.Idle, time.GetUtcNow());

    /// <summary>Ändert den Zustand; der Zeitpunkt "seit" bleibt erhalten, solange der Zustand gleich bleibt.</summary>
    public void Update(Guid cameraId, Func<CameraStatus, CameraStatus> update) =>
        statuses.AddOrUpdate(
            cameraId,
            _ => update(new CameraStatus(CameraState.Idle, time.GetUtcNow())),
            (_, old) =>
            {
                var updated = update(old);
                return updated.State == old.State ? updated with { SinceUtc = old.SinceUtc } : updated with { SinceUtc = time.GetUtcNow() };
            });

    public void Remove(Guid cameraId) => statuses.TryRemove(cameraId, out _);
}
