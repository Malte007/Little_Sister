namespace LittleSister.Infrastructure.Playback;

/// <summary>Ein Kalendertag in der Zeitzone der Installation, als UTC-Zeitraum.</summary>
public readonly record struct RecordingDay(DateOnly Date, DateTimeOffset FromUtc, DateTimeOffset ToUtc)
{
    /// <summary>Tagesgrenzen in der angegebenen Zeitzone; Tage mit Zeitumstellung haben 23 oder 25 Stunden.</summary>
    public static RecordingDay Of(DateOnly date, TimeZoneInfo zone) =>
        new(date, StartOf(date, zone), StartOf(date.AddDays(1), zone));

    /// <summary>Der Tag, in den ein Zeitpunkt in der angegebenen Zeitzone fällt.</summary>
    public static DateOnly DateOf(DateTimeOffset utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, zone).DateTime);

    private static DateTimeOffset StartOf(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Fällt Mitternacht in eine Lücke der Zeitumstellung, beginnt der Tag mit der ersten gültigen Minute
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    /// <summary>Zeitzone der Installation; unbekannte Angaben fallen auf UTC zurück.</summary>
    public static TimeZoneInfo Zone(string? timeZoneId) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId ?? "", out var zone) ? zone : TimeZoneInfo.Utc;
}
