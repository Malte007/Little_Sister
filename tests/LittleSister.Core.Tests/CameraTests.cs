using LittleSister.Core.Cameras;

namespace LittleSister.Core.Tests;

public class CameraTests
{
    [Theory]
    [InlineData("rtsp://192.168.1.20:554/h264Preview_01_main", true)]
    [InlineData("rtsps://cam.local/stream", true)]
    [InlineData("http://192.168.1.20/", false)]
    [InlineData("192.168.1.20", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsRtspUrl(string? url, bool expected) => Assert.Equal(expected, CameraValidator.IsRtspUrl(url));

    [Fact]
    public void Validate_FehlendeFelder()
    {
        var errors = CameraValidator.Validate("", "http://x", "kaputt", 0, 0);
        Assert.Equal(["name", "mainStreamUrl", "subStreamUrl", "quotaGb", "maxRetentionDays"], errors.Keys);
    }

    [Fact]
    public void WithCredentials_KodiertSonderzeichen()
    {
        var url = StreamUrl.WithCredentials("rtsp://192.168.1.20:554/stream", "admin", "p@ss:wort/1");
        Assert.Equal("rtsp://admin:p%40ss%3Awort%2F1@192.168.1.20:554/stream", url);
    }

    [Fact]
    public void WithCredentials_OhneBenutzer_Unveraendert()
    {
        Assert.Equal("rtsp://cam/s", StreamUrl.WithCredentials("rtsp://cam/s", null, "x"));
    }

    [Fact]
    public void WithoutCredentials_TrenntZugangsdatenAb()
    {
        var url = StreamUrl.WithoutCredentials("rtsp://admin:p%40ss@192.168.1.20:554/stream", out var user, out var pass);
        Assert.Equal("rtsp://192.168.1.20:554/stream", url);
        Assert.Equal("admin", user);
        Assert.Equal("p@ss", pass);
    }
}
