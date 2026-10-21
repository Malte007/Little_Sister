using System.Net;

namespace LittleSister.Api.Tests;

public class SystemTests
{
    [Fact]
    public async Task Healthz_OhneAnmeldungErreichbar()
    {
        using var factory = new LittleSisterFactory();
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/healthz")).StatusCode);
    }

    [Fact]
    public async Task UnbekannterApiPfad_404()
    {
        using var factory = new LittleSisterFactory();
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync("/api/gibtsnicht")).StatusCode);
    }

    [Fact]
    public async Task Migration_LegtDatenbankImDatenordnerAn()
    {
        using var factory = new LittleSisterFactory();
        await factory.CreateClient().GetAsync("/healthz");
        Assert.True(File.Exists(Path.Combine(factory.DataPath, "littlesister.db")));
        Assert.True(Directory.Exists(Path.Combine(factory.DataPath, "keys")));
    }
}
