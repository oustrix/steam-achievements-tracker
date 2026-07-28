using Microsoft.Extensions.Logging;
using SteamAchievements.Core.Diagnostics;

namespace SteamAchievements.Core.Tests.Diagnostics;

public class RollingFileLoggerProviderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "satprovider-" + Guid.NewGuid().ToString("N"));

    private static readonly DateTimeOffset At =
        new(2026, 7, 27, 9, 14, 2, 113, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string Text => File.ReadAllText(Path.Combine(_directory, "log.txt"));

    private RollingFileLoggerProvider NewProvider() =>
        new(new LogFileOptions(_directory), () => At);

    [Fact]
    public void WritesAFormattedLineForALoggedMessage()
    {
        using var provider = NewProvider();

        provider.CreateLogger("SteamAchievements.Core.App.SyncCoordinator")
            .LogDebug("sync started force={Force}", false);

        Assert.Equal(
            "2026-07-27 09:14:02.113Z  DBG  SyncCoordinator  sync started force=False\r\n",
            Text);
    }

    [Fact]
    public void WritesEveryLevelBecauseNothingIsFiltered()
    {
        using var provider = NewProvider();
        var log = provider.CreateLogger("X");

        log.LogTrace("t");
        log.LogDebug("d");
        log.LogInformation("i");
        log.LogWarning("w");
        log.LogError("e");
        log.LogCritical("c");

        Assert.Equal(6, Text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void ReportsEveryLevelAsEnabledExceptNone()
    {
        using var provider = NewProvider();
        var log = provider.CreateLogger("X");

        Assert.True(log.IsEnabled(LogLevel.Trace));
        Assert.True(log.IsEnabled(LogLevel.Critical));
        Assert.False(log.IsEnabled(LogLevel.None));
    }

    [Fact]
    public void ScrubsASecretThatReachesTheLineThroughAMessage()
    {
        using var provider = NewProvider();

        provider.CreateLogger("X").LogDebug(
            "GET https://api.steampowered.com/x?key=ABCDEF0123456789ABCDEF0123456789&steamid=7");

        Assert.DoesNotContain("ABCDEF0123456789ABCDEF0123456789", Text);
        Assert.Contains("key=***", Text);
    }

    [Fact]
    public void ScrubsASecretThatReachesTheLineThroughAnException()
    {
        using var provider = NewProvider();

        provider.CreateLogger("X").LogError(
            new InvalidOperationException("failed calling ?key=ABCDEF0123456789ABCDEF0123456789"),
            "request failed");

        Assert.DoesNotContain("ABCDEF0123456789ABCDEF0123456789", Text);
    }

    [Fact]
    public void ReturnsAScopeThatCanBeDisposedWithoutDoingAnything()
    {
        using var provider = NewProvider();

        using (provider.CreateLogger("X").BeginScope("ignored"))
        {
            provider.CreateLogger("X").LogInformation("inside");
        }

        Assert.Contains("inside", Text);
    }

    [Fact]
    public void SharesOneFileAcrossEveryCategory()
    {
        using var provider = NewProvider();

        provider.CreateLogger("A").LogInformation("from a");
        provider.CreateLogger("B").LogInformation("from b");

        Assert.Contains("A  from a", Text);
        Assert.Contains("B  from b", Text);
    }
}
