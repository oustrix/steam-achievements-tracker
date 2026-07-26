using SteamAchievements.Core.App;

namespace SteamAchievements.Core.Tests.App;

public class SteamAccountLocatorTests
{
    [Fact]
    public void FindsNothingWhenSteamIsNotInstalled()
    {
        Assert.Empty(new SteamAccountLocator(new FixedSteamPath(null)).FindAccounts());
        Assert.Null(new SteamAccountLocator(new FixedSteamPath(null)).FindActiveAccount());
    }

    [Fact]
    public void FindsNothingWhenTheSteamFolderHasNoLoginFile()
    {
        var empty = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(empty);
        try
        {
            Assert.Empty(new SteamAccountLocator(new FixedSteamPath(empty)).FindAccounts());
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public void ReadsTheAccountsOutOfTheLoginFile()
    {
        using var steam = new TempSteamRoot();

        Assert.NotEmpty(new SteamAccountLocator(new FixedSteamPath(steam.Path)).FindAccounts());
    }

    [Fact]
    public void PicksTheAccountSteamMarkedMostRecent()
    {
        using var steam = new TempSteamRoot();

        // The committed fixture holds two accounts: 76561190000000001
        // ("olduser", MostRecent 0) and 76561190000000002 ("currentuser",
        // MostRecent 1).
        var active = new SteamAccountLocator(new FixedSteamPath(steam.Path)).FindActiveAccount();

        Assert.NotNull(active);
        Assert.Equal(TempSteamRoot.ActiveSteamId, active.SteamId64);
        Assert.Equal(TempSteamRoot.ActiveAccountName, active.AccountName);
    }

    [Fact]
    public void SurvivesAMalformedLoginFileInsteadOfThrowing()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "config", "loginusers.vdf"), "\"users\"\n{\n  \"7656119\"");
        try
        {
            Assert.Empty(new SteamAccountLocator(new FixedSteamPath(root)).FindAccounts());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
