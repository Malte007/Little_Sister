using LittleSister.Core.Recording;
using LittleSister.Core.Storage;

namespace LittleSister.Core.Tests;

public class RetentionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Erzeugt stündliche Segmente; das erste ist das älteste.</summary>
    private static List<Segment> Hourly(int count, long size = 100) =>
        Enumerable.Range(0, count)
            .Select(i => new Segment
            {
                Id = i + 1,
                StartUtc = Now.AddHours(-count + i),
                EndUtc = Now.AddHours(-count + i + 1),
                SizeBytes = size,
            })
            .ToList();

    [Fact]
    public void Kontingent_AeltesteZuerstBisGenugFrei()
    {
        var delete = RetentionPolicy.SelectToFree(Hourly(10), bytesToFree: 300);
        Assert.Equal([1L, 2L, 3L], delete.Select(s => s.Id));
    }

    [Fact]
    public void GeschuetzteSegmente_BleibenStehen()
    {
        var segments = Hourly(10);
        segments[0].IsProtected = true;
        var delete = RetentionPolicy.SelectToFree(segments, bytesToFree: 300);
        Assert.Equal([2L, 3L, 4L], delete.Select(s => s.Id));
    }

    [Fact]
    public void Stichtag_NurMitMaximalerAufbewahrung()
    {
        Assert.Null(RetentionPolicy.RetentionCutoff(null, Now));
        Assert.Equal(Now.AddDays(-7), RetentionPolicy.RetentionCutoff(7, Now));
    }

    [Fact]
    public void Freispeicher_UeberAlleKameras_AeltesteZuerst()
    {
        var a = Hourly(3);
        var b = Hourly(3);
        b.ForEach(s => { s.Id += 100; s.StartUtc = s.StartUtc.AddMinutes(30); });
        var delete = RetentionPolicy.SelectToFree(a.Concat(b), bytesToFree: 250);
        Assert.Equal([1L, 101L, 2L], delete.Select(s => s.Id));
    }

    [Fact]
    public void Freispeicher_NichtsNoetig()
    {
        Assert.Empty(RetentionPolicy.SelectToFree(Hourly(3), 0));
    }

    [Fact]
    public void Tagesschaetzung()
    {
        // 10 GB in 1 Tag, Kontingent 200 GB → 20 Tage
        const long gb = 1024L * 1024 * 1024;
        Assert.Equal(20, RetentionPolicy.EstimateDays(10 * gb, TimeSpan.FromDays(1), 200 * gb));
        Assert.Null(RetentionPolicy.EstimateDays(10, TimeSpan.FromMinutes(1), 200));
    }
}
