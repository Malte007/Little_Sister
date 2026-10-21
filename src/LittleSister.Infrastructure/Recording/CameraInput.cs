using LittleSister.Core.Cameras;
using LittleSister.Infrastructure.Security;

namespace LittleSister.Infrastructure.Recording;

/// <summary>Liefert die ffmpeg-Eingabeparameter für eine Kamera.</summary>
public interface ICameraInputFactory
{
    IReadOnlyList<string> CreateInputArguments(Camera camera);
}

/// <summary>Standard: RTSP über TCP (stabiler als UDP im WLAN) mit Zeitlimit bei Verbindungsabbruch.</summary>
public sealed class RtspInputFactory(CameraCredentialProtector credentials) : ICameraInputFactory
{
    public IReadOnlyList<string> CreateInputArguments(Camera camera) =>
    [
        "-rtsp_transport", "tcp",
        // Nach 10 s ohne Daten bricht ffmpeg ab, der Recorder startet neu (Angabe in Mikrosekunden)
        "-timeout", "10000000",
        "-i", StreamUrl.WithCredentials(camera.MainStreamUrl, camera.Username, credentials.Unprotect(camera.ProtectedPassword)),
    ];
}
