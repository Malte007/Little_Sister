using LittleSister.Infrastructure.Recording;

namespace LittleSister.Infrastructure.Tests;

public class RecordingTests
{
    [Fact]
    public void SegmentDateiname_WirdAlsUtcGelesen()
    {
        Assert.True(SegmentFiles.TryParseStart("/x/.incoming/20261021T120420Z.mp4", out var start));
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 12, 4, 20, TimeSpan.Zero), start);
        Assert.False(SegmentFiles.TryParseStart("kaputt.mp4", out _));
    }

    [Fact]
    public void EndgueltigerPfad_NachTagSortiert()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var path = SegmentFiles.FinalRelativePath(id, new DateTimeOffset(2026, 10, 21, 12, 4, 20, TimeSpan.Zero));
        Assert.Equal(Path.Combine("11111111222233334444555555555555", "20261021", "120420.mp4"), path);
    }

    [Fact]
    public void FfmpegArgumente_KopierenVideoOhneUmwandlung()
    {
        var args = CameraRecorder.BuildArguments(["-i", "rtsp://cam/stream"], "/rec/.incoming", 60);
        var joined = string.Join(' ', args);
        Assert.Contains("-c:v copy", joined);
        Assert.Contains("-segment_time 60", joined);
        Assert.Contains("-segment_list pipe:1", joined);
        Assert.Equal(Path.Combine("/rec/.incoming", SegmentFiles.IncomingPattern), args[^1]);
    }

    [Theory]
    [InlineData("method DESCRIBE failed: 401 Unauthorized", "streamUnauthorized")]
    [InlineData("method DESCRIBE failed: 404 Not Found", "streamNotFound")]
    [InlineData("Connection refused", "streamRefused")]
    [InlineData("irgendwas", "streamUnreachable")]
    public void StreamFehler_WerdenEingeordnet(string stderr, string code) =>
        Assert.Equal(code, StreamProbe.ClassifyError(stderr));
}
