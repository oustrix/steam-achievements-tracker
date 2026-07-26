using SteamAchievements.Core.App;

namespace SteamAchievements.Core.Tests.App;

public class ApiKeyTests
{
    private const string Valid = "0123456789abcdef0123456789ABCDEF";

    [Fact]
    public void AcceptsThirtyTwoHexCharacters()
    {
        Assert.True(ApiKey.TryNormalize(Valid, out var normalized));
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF", normalized);
    }

    [Theory]
    [InlineData("  0123456789abcdef0123456789ABCDEF  ")]
    [InlineData("\n0123456789abcdef0123456789ABCDEF\r\n")]
    [InlineData("\"0123456789abcdef0123456789ABCDEF\"")]
    [InlineData("'0123456789abcdef0123456789ABCDEF'")]
    public void SurvivesWhatAPasteAddsAroundIt(string pasted)
    {
        Assert.True(ApiKey.TryNormalize(pasted, out var normalized));
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF", normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789ABCDE")]
    [InlineData("0123456789abcdef0123456789ABCDEFF")]
    [InlineData("0123456789abcdef0123456789ABCDEG")]
    [InlineData("0123456789abcdef 123456789ABCDEF")]
    public void RejectsAnythingElse(string? candidate)
    {
        Assert.False(ApiKey.TryNormalize(candidate, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }
}
