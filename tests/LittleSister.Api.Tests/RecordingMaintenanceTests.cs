using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LittleSister.Core.Events;
using LittleSister.Core.Recording;
using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Recording;
using LittleSister.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LittleSister.Api.Tests;

/// <summary>Unbekannte Aufnahmen wieder einlesen und alle Aufnahmen einer Kamera löschen.</summary>
public class RecordingMaintenanceTests
{
    private sealed class FakeDurations : ISegmentDurationProbe
    {
        public Task<TimeSpan?> GetDurationAsync(string file, CancellationToken ct) =>
            Task.FromResult<TimeSpan?>(new FileInfo(file).Length > 1 ? TimeSpan.FromMinutes(1) : null);
    }

    /// <summary>Kamera mit minütlichen Segmenten (Dateien älter als zwei Minuten und Datenbank).</summary>
    private static async Task<(Guid Id, List<string> Files)> SeedAsync(LittleSisterFactory factory, HttpClient admin, int count)
    {
        var response = await admin.PostAsJsonAsync("/api/cameras", new { name = "Garten", mainStreamUrl = "rtsp://192.168.1.20/s", recordingEnabled = false, quotaGb = 100 });
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();
        var files = new List<string>();
        var first = new DateTimeOffset(DateTimeOffset.UtcNow.Date.AddHours(-12), TimeSpan.Zero);
        for (var i = 0; i < count; i++)
        {
            var start = first.AddMinutes(i);
            var relative = SegmentFiles.FinalRelativePath(id, start);
            var file = Path.Combine(factory.RecordingsPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllBytesAsync(file, new byte[16]);
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-1));
            files.Add(file);
            db.Segments.Add(new Segment { CameraId = id, StorageRoot = factory.RecordingsPath, StartUtc = start, EndUtc = start.AddMinutes(1), RelativePath = relative, SizeBytes = 16 });
        }
        db.Events.Add(new CameraEvent { CameraId = id, Kind = "motion", StartUtc = first, EndUtc = first.AddSeconds(5) });
        await db.SaveChangesAsync();
        return (id, files);
    }

    private static async Task<List<Segment>> SegmentsAsync(LittleSisterFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>().Segments.OrderBy(s => s.StartUtc).ToListAsync();
    }

    [Fact]
    public async Task Einlesen_TraegtFehlendeSegmenteNachSchemaEin()
    {
        using var factory = new LittleSisterFactory { ConfigureServices = s => s.AddSingleton<ISegmentDurationProbe, FakeDurations>() };
        var admin = await factory.CreateAdminClientAsync();
        var (id, files) = await SeedAsync(factory, admin, count: 5);

        // Zwei Einträge gehen verloren (z. B. Ringspeicher bei nicht eingehängter Freigabe), die Dateien bleiben
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();
            db.Segments.RemoveRange(await db.Segments.OrderBy(s => s.StartUtc).Take(2).ToListAsync());
            await db.SaveChangesAsync();
        }
        // Passen nicht zum Schema bzw. sind zu frisch: bleiben außen vor
        var incoming = Path.Combine(SegmentFiles.IncomingDirectory(factory.RecordingsPath, id), "20261021T120420Z.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(incoming)!);
        await File.WriteAllBytesAsync(incoming, new byte[16]);
        var fresh = Path.Combine(Path.GetDirectoryName(files[0])!, "235959.mp4");
        await File.WriteAllBytesAsync(fresh, new byte[16]);
        await File.WriteAllBytesAsync(Path.Combine(Path.GetDirectoryName(files[0])!, "notiz.mp4"), new byte[16]);

        var importer = factory.Services.GetRequiredService<SegmentImporter>();
        Assert.Equal(2, await importer.RunOnceAsync(CancellationToken.None));
        Assert.Equal(0, await importer.RunOnceAsync(CancellationToken.None));

        var segments = await SegmentsAsync(factory);
        Assert.Equal(5, segments.Count);
        Assert.All(segments, s => Assert.Equal(TimeSpan.FromMinutes(1), s.EndUtc - s.StartUtc));
    }

    [Fact]
    public async Task Einlesen_OhneKennung_NichtsPassiert()
    {
        using var factory = new LittleSisterFactory { ConfigureServices = s => s.AddSingleton<ISegmentDurationProbe, FakeDurations>() };
        var admin = await factory.CreateAdminClientAsync();
        var (_, files) = await SeedAsync(factory, admin, count: 3);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>().Segments.ExecuteDeleteAsync();
        File.Delete(StorageMarker.PathOf(factory.RecordingsPath));
        // Ohne bekannte Aufnahmen würde die Kennung neu entstehen; ein bekannter, fehlender Eintrag verhindert das
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();
            var cameraId = await db.Cameras.Select(c => c.Id).FirstAsync();
            db.Segments.Add(new Segment { CameraId = cameraId, StorageRoot = factory.RecordingsPath, StartUtc = DateTimeOffset.UtcNow.AddDays(-2), EndUtc = DateTimeOffset.UtcNow.AddDays(-2).AddMinutes(1), RelativePath = "fehlt.mp4", SizeBytes = 16 });
            await db.SaveChangesAsync();
        }

        Assert.Null(await factory.Services.GetRequiredService<SegmentImporter>().RunOnceAsync(CancellationToken.None));
        Assert.Single(await SegmentsAsync(factory));
        Assert.All(files, f => Assert.True(File.Exists(f)));
    }

    [Fact]
    public async Task AlleAufnahmenLoeschen_NurAdmin_EntferntSegmenteDateienUndEreignisse()
    {
        using var factory = new LittleSisterFactory();
        var admin = await factory.CreateAdminClientAsync();
        var (id, files) = await SeedAsync(factory, admin, count: 4);

        var viewer = await factory.CreateUserClientAsync(admin, "betrachter", "Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/cameras/{id}/recordings")).StatusCode);

        var response = await admin.DeleteAsync($"/api/cameras/{id}/recordings");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, result.GetProperty("deleted").GetInt32());
        Assert.Equal(0, result.GetProperty("skipped").GetInt32());

        Assert.Empty(await SegmentsAsync(factory));
        Assert.All(files, f => Assert.False(File.Exists(f)));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LittleSisterDbContext>();
        Assert.Empty(await db.Events.ToListAsync());
        Assert.True(await db.Cameras.AnyAsync(c => c.Id == id));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/cameras/{Guid.NewGuid()}/recordings")).StatusCode);
    }
}
