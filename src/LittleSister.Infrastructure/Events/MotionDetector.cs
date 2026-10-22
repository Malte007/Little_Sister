namespace LittleSister.Infrastructure.Events;

/// <summary>
/// Erkennt Bewegung in kleinen Graustufenbildern per Differenz zu einem gleitenden Hintergrund.
/// Je Bild eine Subtraktion pro Pixel; bei 80×60 Pixeln und 5 Bildern/s praktisch ohne CPU-Last.
/// </summary>
public sealed class MotionDetector
{
    /// <summary>Ab dieser Helligkeitsdifferenz (0–255) gilt ein Pixel als verändert; darunter liegt Bildrauschen.</summary>
    public const int PixelThreshold = 25;

    /// <summary>Ändert sich mehr als dieser Anteil auf einmal, war es Licht (Lampe, Nachtsicht), keine Bewegung.</summary>
    public const double LightingChange = 0.6;

    // Anteil, mit dem jedes neue Bild in den Hintergrund eingeht; langsame Änderungen (Schatten, Wolken) verschwinden so
    private const float BackgroundRate = 0.05f;

    private readonly float threshold;
    private readonly int startFrames;
    private readonly int quietFrames;
    private float[]? background;
    private int hits;
    private int quiet;

    /// <param name="sensitivity">1 (nur große Bewegung) bis 100 (schon kleinste Änderung).</param>
    /// <param name="framesPerSecond">Bildrate der Analyse, für die Zeitangaben.</param>
    public MotionDetector(int sensitivity, int framesPerSecond)
    {
        threshold = (float)AreaThreshold(sensitivity);
        // Beginn erst nach zwei auffälligen Bildern in Folge (gegen einzelne Störungen), Ende nach 3 s Ruhe
        startFrames = 2;
        quietFrames = Math.Max(1, framesPerSecond * 3);
    }

    /// <summary>Läuft gerade eine Bewegung?</summary>
    public bool Active { get; private set; }

    /// <summary>Anteil veränderter Pixel im letzten Bild (0–1); hilft beim Einstellen der Empfindlichkeit.</summary>
    public double LastChange { get; private set; }

    /// <summary>
    /// Mindestanteil veränderter Pixel: Empfindlichkeit 100 → 0,2 %, 50 → etwa 1,4 %, 1 → 10 % des Bildes.
    /// Exponentiell, damit der Regler im empfindlichen Bereich fein bleibt.
    /// </summary>
    public static double AreaThreshold(int sensitivity) =>
        0.002 * Math.Pow(50, (100 - Math.Clamp(sensitivity, 1, 100)) / 99.0);

    /// <summary>Verarbeitet ein Bild (ein Byte je Pixel).</summary>
    /// <returns>Den neuen Zustand, wenn er sich geändert hat, sonst null.</returns>
    public bool? Process(ReadOnlySpan<byte> frame)
    {
        if (background is null || background.Length != frame.Length)
        {
            background = new float[frame.Length];
            for (var i = 0; i < frame.Length; i++)
                background[i] = frame[i];
            return null;
        }

        var changed = 0;
        for (var i = 0; i < frame.Length; i++)
        {
            var value = frame[i];
            var diff = value - background[i];
            if (diff > PixelThreshold || diff < -PixelThreshold)
                changed++;
            background[i] += (value - background[i]) * BackgroundRate;
        }

        var share = (double)changed / frame.Length;
        LastChange = share;

        if (share > LightingChange)
        {
            // Lichtwechsel: neuen Hintergrund übernehmen, Zustand nicht ändern
            for (var i = 0; i < frame.Length; i++)
                background[i] = frame[i];
            hits = 0;
            return null;
        }

        if (share >= threshold)
        {
            quiet = 0;
            if (!Active && ++hits >= startFrames)
                return Active = true;
        }
        else
        {
            hits = 0;
            if (Active && ++quiet >= quietFrames)
                return Active = false;
        }
        return null;
    }
}
