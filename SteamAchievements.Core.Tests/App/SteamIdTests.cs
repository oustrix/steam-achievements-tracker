using SteamAchievements.Core.App;

namespace SteamAchievements.Core.Tests.App;

public class SteamIdTests
{
    [Fact]
    public void AcceptsASeventeenDigitId()
    {
        Assert.True(SteamId.TryParse("76561190000000002", out var parsed));
        Assert.Equal(76561190000000002UL, parsed);
    }

    [Fact]
    public void AcceptsAProfileUrl()
    {
        Assert.True(SteamId.TryParse("https://steamcommunity.com/profiles/76561190000000002/", out var parsed));
        Assert.Equal(76561190000000002UL, parsed);
    }

    [Fact]
    public void AcceptsAProfileUrlWithoutATrailingSlash()
    {
        Assert.True(SteamId.TryParse("https://steamcommunity.com/profiles/76561190000000002", out var parsed));
        Assert.Equal(76561190000000002UL, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7656119")]
    [InlineData("not a number")]
    [InlineData("https://steamcommunity.com/id/oustrix")]
    public void RejectsAnythingElseIncludingVanityUrls(string? candidate)
    {
        Assert.False(SteamId.TryParse(candidate, out var parsed));
        Assert.Equal(0UL, parsed);
    }
}
