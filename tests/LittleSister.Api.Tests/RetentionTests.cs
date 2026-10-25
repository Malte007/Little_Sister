using System.Net.Http.Json;
using System.Text.Json;
using LittleSister.Core.Recording;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Recording;
using LittleSister.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LittleSister.Api.Tests;

public class RetentionTests
{
    private sealed class FakeFreeSpace : IFreeSpaceProvider
    {
        public long? Available { get; set; } = long.MaxValue;
        public long? GetAvailableBytes(string path) => Available;
    }

    /// <summary>Legt eine Kamera mit stündlichen Segmenten à 1 MB an (Dateien und Datenbank).</summary>
    private static async Task<Guid> SeedAsync(LittleSisterFactory factory, HttpClient admin, int hours, double quotaGb, int? maxDays)
    {
        var response = await admin.PostAsJsonAsync("/api/cameras", new
        {
            name = "Garten", mainStreamUrl = "rtsp://192.168.1.20/s", recordingEnabled = false, quotaGb, maxRetentionDays = maxDays,
        });
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < hours; i++)
        {
            var start = now.AddHours(-hours + i);
            var relative = SegmentFiles.FinalRelativePath(id, start);
            var file = Path.Combine(factory.RecordingsPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllBytesAsync(file, new byte[16]);
            db.Segments.Add(new Segment { CameraId = id, StorageRoot = factory.RecordingsPath, StartUtc = start, EndUtc = start.AddHours(1), RelativePath = relative, SizeBytes = 1024 * 1024 });
        }
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<List<Segment>> SegmentsAsync(LittleSisterFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>().Segments.OrderBy(s => s.StartUtc).ToListAsync();
    }

    [Fact]
    public async Task MaximaleAufbewahrung_LoeschtAlteSegmenteUndDateien()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory, admin, hours: 48, quotaGb: 100, maxDays: 1);

        var deleted = await factory.Services.GetRequiredService<RetentionService>().RunOnceAsync(CancellationToken.None);

        var remaining = await SegmentsAsync(factory);
        Assert.InRange(deleted, 23, 24);
        Assert.All(remaining, s => Assert.True(s.EndUtc >= DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(-1)));
        Assert.All(remaining, s => Assert.True(File.Exists(Path.Combine(factory.RecordingsPath, s.RelativePath))));
    }

    [Fact]
    public async Task Freispeichergrenze_LoeschtAeltesteBisGenugFrei()
    {
        var freeSpace = new FakeFreeSpace();
        using var factory = new LittleSisterFactory { ConfigureServices = s => s.AddSingleton<IFreeSpaceProvider>(freeSpace) };
        var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory, admin, hours: 10, quotaGb: 100, maxDays: null);
        (await admin.PutAsJsonAsync("/api/storage", new { minFreeGb = 1 })).EnsureSuccessStatusCode();

        // Es fehlen 3 MB bis zur Grenze → die 3 ältesten Segmente müssen weg
        freeSpace.Available = 1024L * 1024 * 1024 - 3 * 1024 * 1024;
        var deleted = await factory.Services.GetRequiredService<RetentionService>().RunOnceAsync(CancellationToken.None);

        Assert.Equal(3, deleted);
        Assert.Equal(7, (await SegmentsAsync(factory)).Count);
    }

    [Fact]
    public async Task GeschuetzteSegmente_UeberlebenDenRingspeicher()
    {
        var freeSpace = new FakeFreeSpace();
        using var factory = new LittleSisterFactory { ConfigureServices = s => s.AddSingleton<IFreeSpaceProvider>(freeSpace) };
        var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory, admin, hours: 5, quotaGb: 100, maxDays: null);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();
            (await db.Segments.OrderBy(s => s.StartUtc).FirstAsync()).IsProtected = true;
            await db.SaveChangesAsync();
        }

        freeSpace.Available = 0; // Platte voll: alles Ungeschützte muss weg
        await factory.Services.GetRequiredService<RetentionService>().RunOnceAsync(CancellationToken.None);

        var remaining = Assert.Single(await SegmentsAsync(factory));
        Assert.True(remaining.IsProtected);
    }

    [Fact]
    public async Task NachOrdnerwechsel_WerdenAlteAufnahmenImAltenOrdnerGeloescht()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory, admin, hours: 48, quotaGb: 100, maxDays: 1);
        var oldFiles = (await SegmentsAsync(factory)).Select(s => s.FullPath).ToList();

        var newPath = Path.Combine(factory.DataPath, "neue-platte");
        (await admin.PutAsJsonAsync("/api/storage", new { minFreeGb = 0, recordingsPath = newPath })).EnsureSuccessStatusCode();
        await factory.Services.GetRequiredService<RetentionService>().RunOnceAsync(CancellationToken.None);

        var remaining = await SegmentsAsync(factory);
        Assert.All(remaining, s => Assert.Equal(factory.RecordingsPath, s.StorageRoot));
        Assert.Equal(oldFiles.Count - remaining.Count, oldFiles.Count(f => !File.Exists(f)));
    }

    [Fact]
    public async Task Ereignisse_OhneAufnahmeWerdenGeloescht()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        var id = await SeedAsync(factory, admin, hours: 10, quotaGb: 100, maxDays: null);
        var oldest = (await SegmentsAsync(factory))[0].StartUtc;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();
            // Lange vor der ältesten Aufnahme, kurz davor (Aufnahme begann verzögert), mittendrin
            foreach (var start in new[] { oldest.AddHours(-3), oldest.AddSeconds(-10), oldest.AddHours(2) })
                db.Events.Add(new LittleSister.Core.Events.CameraEvent { CameraId = id, Kind = "motion", StartUtc = start, EndUtc = start.AddSeconds(5) });
            await db.SaveChangesAsync();
        }

        await factory.Services.GetRequiredService<RetentionService>().RunOnceAsync(CancellationToken.None);

        using var check = factory.Services.CreateScope();
        var remaining = await check.ServiceProvider.GetRequiredService<LittleSisterDbContext>().Events.OrderBy(e => e.StartUtc).ToListAsync();
        Assert.Equal([oldest.AddSeconds(-10), oldest.AddHours(2)], remaining.Select(e => e.StartUtc));
    }

    [Fact]
    public async Task FehlendeKennung_UndFehlendeDateien_NichtsWirdGeloescht()
    {
        var freeSpace = new FakeFreeSpace();
        using var factory = new LittleSisterFactory { ConfigureServices = s => s.AddSingleton<IFreeSpaceProvider>(freeSpace) };
        var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory, admin, hours: 48, quotaGb: 100, maxDays: 1);

        // Freigabe nicht eingehängt: leerer Einhängepunkt auf einer kleinen Platte
        Directory.Delete(factory.RecordingsPath, recursive: true);
        Directory.CreateDirectory(factory.RecordingsPath);
        freeSpace.Available = 0;

        var deleted = await factory.Services.GetRequiredService<RetentionService>().RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.Equal(48, (await SegmentsAsync(factory)).Count);
        Assert.False(File.Exists(StorageMarker.PathOf(factory.RecordingsPath)));
    }

    [Fact]
    public async Task FehlendeKennung_BeiVorhandenenAufnahmen_WirdAngelegt()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory, admin, hours: 48, quotaGb: 100, maxDays: 1);
        Assert.True(File.Exists(StorageMarker.PathOf(factory.RecordingsPath))); // von der Einrichtung angelegt
        File.Delete(StorageMarker.PathOf(factory.RecordingsPath)); // Installation von vor der Kennung

        var deleted = await factory.Services.GetRequiredService<RetentionService>().RunOnceAsync(CancellationToken.None);

        Assert.InRange(deleted, 23, 24);
        Assert.True(File.Exists(StorageMarker.PathOf(factory.RecordingsPath)));
    }
}
