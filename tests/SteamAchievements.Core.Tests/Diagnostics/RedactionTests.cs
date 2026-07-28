using SteamAchievements.Core.Diagnostics;

namespace SteamAchievements.Core.Tests.Diagnostics;

public class RedactionTests
{
    [Fact]
    public void StripsTheKeyFromAQueryStringButKeepsEverythingElse()
    {
        const string url =
            "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/"
            + "?key=ABCDEF0123456789ABCDEF0123456789&steamid=76561198000000000&format=json";

        Assert.Equal(
            "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/"
            + "?key=***&steamid=76561198000000000&format=json",
            Redaction.Scrub(url));
    }

    [Fact]
    public void StripsAnAccessTokenAndKeepsItsParameterName()
    {
        Assert.Equal(
            "GET /x?access_token=***&b=2",
            Redaction.Scrub("GET /x?access_token=9f8e7d6c5b4a&b=2"));
    }

    [Fact]
    public void MatchesTheParameterNameCaseInsensitively()
    {
        Assert.Equal("?KEY=***", Redaction.Scrub("?KEY=ABCDEF0123456789ABCDEF0123456789"));
    }

    [Fact]
    public void LeavesAParameterThatMerelyEndsInKeyAlone()
    {
        Assert.Equal("?monkey=banana", Redaction.Scrub("?monkey=banana"));
    }

    [Fact]
    public void StripsABareTokenShapedLikeAnApiKey()
    {
        Assert.Equal(
            "Steam rejected ***",
            Redaction.Scrub("Steam rejected ABCDEF0123456789ABCDEF0123456789"));
    }

    [Fact]
    public void StripsABareTokenShapedLikeAnApiKeyInLowercase()
    {
        // The CLI reads the key from --key/STEAM_API_KEY without normalising
        // its case (ApiKey.TryNormalize only runs in the onboarding path), so
        // a key typed in lowercase is a real credential that must be caught
        // too, not just the uppercase form Steam issues.
        Assert.Equal(
            "Steam rejected ***",
            Redaction.Scrub("Steam rejected abcdef0123456789abcdef0123456789"));
    }

    [Fact]
    public void LeavesAFortyCharacterLowercaseHashAlone()
    {
        // Achievement icon URLs carry SHA-1 hashes. Scrubbing those would empty
        // the log of the URLs it exists to record. This is now the test that
        // proves length — not case — is what protects icon URLs: the mask
        // regex accepts lowercase hex, and only rejects this string because
        // it is 40 characters long rather than 32.
        const string icon = "https://media.steampowered.com/a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4.jpg";

        Assert.Equal(icon, Redaction.Scrub(icon));
    }

    [Fact]
    public void LeavesAKeyShapedRunThatIsPartOfALongerTokenAlone()
    {
        Assert.Equal(
            "XABCDEF0123456789ABCDEF0123456789X",
            Redaction.Scrub("XABCDEF0123456789ABCDEF0123456789X"));
    }

    [Fact]
    public void PassesAnOrdinaryMessageThroughUnchanged()
    {
        Assert.Equal("sync started force=False", Redaction.Scrub("sync started force=False"));
    }

    [Fact]
    public void HandlesAnEmptyString()
    {
        Assert.Equal("", Redaction.Scrub(""));
    }
}
