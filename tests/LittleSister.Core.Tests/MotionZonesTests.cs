using LittleSister.Core.Cameras;

namespace LittleSister.Core.Tests;

public class MotionZonesTests
{
    /// <summary>Nur die linke Bildhälfte überwacht.</summary>
    private static string LeftHalf() =>
        string.Concat(Enumerable.Repeat(new string('1', 8) + new string('0', 8), MotionZones.Rows));

    [Fact]
    public void Gueltig_NurVolleLaengeMitAktivemFeld()
    {
        Assert.True(MotionZones.IsValid(LeftHalf()));
        Assert.False(MotionZones.IsValid(new string('0', MotionZones.Cells)));
        Assert.False(MotionZones.IsValid("101"));
        Assert.False(MotionZones.IsValid(LeftHalf().Replace('0', 'x')));
        Assert.False(MotionZones.IsValid(null));
    }

    [Fact]
    public void Maske_UebertraegtRasterAufPixel()
    {
        // 80×60 Pixel: jedes Feld deckt 5×5 Pixel
        var mask = MotionZones.ToMask(LeftHalf(), 80, 60)!;
        Assert.True(mask[0]);
        Assert.True(mask[39]);
        Assert.False(mask[40]);
        Assert.True(mask[59 * 80 + 39]);
        Assert.False(mask[59 * 80 + 79]);
    }

    [Fact]
    public void Maske_GanzesBildOhneBereiche()
    {
        Assert.Null(MotionZones.ToMask(null, 80, 60));
        Assert.Null(MotionZones.ToMask(new string('1', MotionZones.Cells), 80, 60));
    }

    [Fact]
    public void Pruefung_LehntUngueltigeBereicheAb()
    {
        var errors = CameraValidator.Validate("Hof", "rtsp://cam/1", null, 10, null, zones: new string('0', MotionZones.Cells));
        Assert.Equal(["invalidZones"], errors["motionZones"]);
    }
}
