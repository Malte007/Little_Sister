using LittleSister.Core.Cameras;
using LittleSister.Infrastructure.Security;

namespace LittleSister.Infrastructure.Live;

/// <summary>Namen und Quellen der go2rtc-Streams einer Kamera.</summary>
public static class LiveStreams
{
    /// <summary>Stream für Kacheln: Substream, ersatzweise Hauptstream.</summary>
    public static string Name(Guid cameraId) => cameraId.ToString("N");

    /// <summary>Hauptstream in voller Auflösung; nur vorhanden, wenn es auch einen Substream gibt.</summary>
    public static string HdName(Guid cameraId) => cameraId.ToString("N") + "_hd";
}

/// <summary>Liefert die go2rtc-Quelle eines Streams (austauschbar für Tests).</summary>
public interface ILiveSourceFactory
{
    string CreateSource(Camera camera, string url);
}

/// <summary>Standard: RTSP mit Zugangsdaten; ohne Rückkanal, den manche Kameras (z. B. Reolink) nicht vertragen.</summary>
public sealed class RtspLiveSourceFactory(CameraCredentialProtector credentials) : ILiveSourceFactory
{
    public string CreateSource(Camera camera, string url) =>
        StreamUrl.WithCredentials(url, camera.Username, credentials.Unprotect(camera.ProtectedPassword)) + "#backchannel=0";
}
