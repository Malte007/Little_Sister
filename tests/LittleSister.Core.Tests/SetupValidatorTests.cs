using LittleSister.Core.Setup;

namespace LittleSister.Core.Tests;

public class SetupValidatorTests
{
    // Plattformabhängig absoluter Pfad, damit die Tests unter Linux und Windows laufen
    private static SetupRequest Valid() =>
        new("admin", "geheim-123456", "de", "Europe/Berlin", Path.Combine(Path.GetTempPath(), "recordings"));

    [Fact]
    public void Validate_GueltigeEingaben_OhneFehler()
    {
        Assert.Empty(SetupValidator.Validate(Valid()));
    }

    [Fact]
    public void Validate_UnbekannteSprache_Fehler()
    {
        var errors = SetupValidator.Validate(Valid() with { Language = "fr" });
        Assert.Contains(nameof(SetupRequest.Language), errors.Keys);
    }

    [Fact]
    public void Validate_UnbekannteZeitzone_Fehler()
    {
        var errors = SetupValidator.Validate(Valid() with { TimeZoneId = "Mond/Krater" });
        Assert.Contains(nameof(SetupRequest.TimeZoneId), errors.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("recordings")]
    [InlineData("./aufnahmen")]
    public void Validate_RelativerOderLeererPfad_Fehler(string path)
    {
        var errors = SetupValidator.Validate(Valid() with { RecordingsPath = path });
        Assert.Contains(nameof(SetupRequest.RecordingsPath), errors.Keys);
    }

    [Fact]
    public void Validate_LeererBenutzername_Fehler()
    {
        var errors = SetupValidator.Validate(Valid() with { UserName = "  " });
        Assert.Contains(nameof(SetupRequest.UserName), errors.Keys);
    }
}
