namespace LittleSister.Core.Cameras;

/// <summary>
/// Erkennungsbereiche als Raster über dem Bild: je Feld '1' = überwacht, '0' = ignoriert,
/// zeilenweise von links oben. null bzw. leer = ganzes Bild.
/// </summary>
public static class MotionZones
{
    public const int Columns = 16;
    public const int Rows = 12;
    public const int Cells = Columns * Rows;

    /// <summary>Gültig: genau <see cref="Cells"/> Zeichen aus '0'/'1', mindestens ein Feld aktiv.</summary>
    public static bool IsValid(string? zones) =>
        zones is { Length: Cells } && zones.All(c => c is '0' or '1') && zones.Contains('1');

    /// <summary>Überträgt das Raster auf ein Bild der Größe width×height (ein Eintrag je Pixel); null = ganzes Bild.</summary>
    public static bool[]? ToMask(string? zones, int width, int height)
    {
        if (zones is null || !IsValid(zones) || zones.All(c => c == '1'))
            return null;

        var mask = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * Rows / height;
            for (var x = 0; x < width; x++)
                mask[y * width + x] = zones[row * Columns + x * Columns / width] == '1';
        }
        return mask;
    }
}
