using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.App;

namespace SteamAchievements.Core.Tests.App;

public class SteamAccountLocatorTests
{
    private sealed class FixedPath(string? path) : ISteamPathProvider
    {
        public string? FindSteamPath() => path;
    }

    /// <summary>
    /// The locator expects a Steam root and looks for config/loginusers.vdf
    /// underneath it, so the committed fixture is copied into that shape.
    /// </summary>
    private static string SteamRootWithFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.Copy(TestPaths.Data("loginusers.vdf"), Path.Combine(root, "config", "loginusers.vdf"));
        return root;
    }

    [Fact]
    public void FindsNothingWhenSteamIsNotInstalled()
    {
        Assert.Empty(new SteamAccountLocator(new FixedPath(null)).FindAccounts());
        Assert.Null(new SteamAccountLocator(new FixedPath(null)).FindActiveAccount());
    }

    [Fact]
    public void FindsNothingWhenTheSteamFolderHasNoLoginFile()
    {
        var empty = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(empty);
        try
        {
            Assert.Empty(new SteamAccountLocator(new FixedPath(empty)).FindAccounts());
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public void ReadsTheAccountsOutOfTheLoginFile()
    {
        var root = SteamRootWithFixture();
        try
        {
            Assert.NotEmpty(new SteamAccountLocator(new FixedPath(root)).FindAccounts());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PicksTheAccountSteamMarkedMostRecent()
    {
        var root = SteamRootWithFixture();
        try
        {
            // The committed fixture holds two accounts: 76561190000000001
            // ("olduser", MostRecent 0) and 76561190000000002 ("currentuser",
            // MostRecent 1).
            var active = new SteamAccountLocator(new FixedPath(root)).FindActiveAccount();

            Assert.NotNull(active);
            Assert.Equal(76561190000000002UL, active.SteamId64);
            Assert.Equal("currentuser", active.AccountName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SurvivesAMalformedLoginFileInsteadOfThrowing()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "config", "loginusers.vdf"), "\"users\"\n{\n  \"7656119\"");
        try
        {
            Assert.Empty(new SteamAccountLocator(new FixedPath(root)).FindAccounts());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
