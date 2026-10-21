using LittleSister.Core.Security;

namespace LittleSister.Core.Tests;

public class RolesTests
{
    [Theory]
    [InlineData("admin", Roles.Admin)]
    [InlineData(" Viewer ", Roles.Viewer)]
    public void TryNormalize_BekannteRolle_LiefertKanonischenNamen(string input, string expected)
    {
        Assert.True(Roles.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Superuser")]
    public void TryNormalize_UnbekannteRolle_IstUngueltig(string? input)
    {
        Assert.False(Roles.TryNormalize(input, out _));
    }
}
