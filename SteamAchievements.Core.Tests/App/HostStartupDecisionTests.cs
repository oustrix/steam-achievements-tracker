using SteamAchievements.Core.App;

namespace SteamAchievements.Core.Tests.App;

public class HostStartupDecisionTests
{
    private const string Folder = @"C:\Users\someone\AppData\Local\SteamAchievementsTracker";

    [Fact]
    public void ShowsNothingWhenTheApplicationCanRun()
    {
        Assert.Null(HostStartupDecision.Evaluate(failureMessage: null, webViewInstalled: true, Folder));
    }

    [Fact]
    public void ExplainsAMissingWebViewRuntimeAndOffersTheInstaller()
    {
        var placard = HostStartupDecision.Evaluate(failureMessage: null, webViewInstalled: false, Folder);

        Assert.NotNull(placard);
        Assert.Contains("WebView2", placard.Message);
        Assert.Equal(HostStartupDecision.WebView2DownloadPage, placard.ActionTarget);
    }

    [Fact]
    public void ExplainsAFailedDatabaseAndPointsAtTheDataFolder()
    {
        var placard = HostStartupDecision.Evaluate("database is locked", webViewInstalled: true, Folder);

        Assert.NotNull(placard);
        Assert.Contains("database is locked", placard.Message);
        Assert.Contains(Folder, placard.Message);
        Assert.Equal(Folder, placard.ActionTarget);
    }

    [Fact]
    public void PrefersTheDatabaseFailureWhenBothAreWrong()
    {
        var placard = HostStartupDecision.Evaluate("database is locked", webViewInstalled: false, Folder);

        Assert.NotNull(placard);
        Assert.Contains("database", placard.Message);
        Assert.DoesNotContain("WebView2", placard.Message);
    }
}
