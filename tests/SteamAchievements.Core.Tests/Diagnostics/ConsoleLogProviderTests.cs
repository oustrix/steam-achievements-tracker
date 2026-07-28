using Microsoft.Extensions.Logging;
using SteamAchievements.Core.Diagnostics;

namespace SteamAchievements.Core.Tests.Diagnostics;

public class ConsoleLogProviderTests
{
    private static readonly DateTimeOffset At =
        new(2026, 7, 27, 9, 14, 2, 113, TimeSpan.Zero);

    private static ConsoleLogProvider NewProvider(StringWriter console) =>
        new(console, () => At);

    /// <summary>
    /// The credential-exposure case this whole class exists to close: the CLI
    /// holds a real Steam Web API key, LoggingHandler logs every request URL
    /// at Debug, and that URL carries the key in its query string. Routed
    /// through the stock Microsoft.Extensions.Logging.Console provider
    /// (AddSimpleConsole), that key would print unredacted. This test proves
    /// ConsoleLogProvider does not repeat that mistake — the same way
    /// RollingFileLoggerProviderTests.ScrubsASecretThatReachesTheLineThroughAMessage
    /// proves it for the file sink, redirecting to a StringWriter instead of a
    /// file so the assertion can run against captured output directly.
    /// </summary>
    [Fact]
    public void ScrubsAKeyShapedSecretInAUrlBeforeItReachesTheConsole()
    {
        using var console = new StringWriter();
        using var provider = NewProvider(console);

        provider.CreateLogger("X").LogDebug(
            "GET https://api.steampowered.com/x?key=ABCDEF0123456789ABCDEF0123456789&steamid=7 -> 200 in 12ms");

        var text = console.ToString();

        Assert.DoesNotContain("ABCDEF0123456789ABCDEF0123456789", text);
        Assert.Contains("key=***", text);
        Assert.Contains("steamid=7", text);
    }

    /// <summary>
    /// Ties the console sink's output shape to the file sink's rather than
    /// letting the two drift apart. Both derive from TextLoggerProvider and
    /// share LogLine.Format and Redaction.Scrub, so for the same input at the
    /// same instant the lines must be byte-for-byte identical — proving that
    /// structurally, not just by inspection of the shared base class.
    /// </summary>
    [Fact]
    public void ProducesTheSameLineShapeAsTheFileProvider()
    {
        var directory = Path.Combine(Path.GetTempPath(), "satconsole-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var console = new StringWriter();
            using var consoleProvider = NewProvider(console);
            using var fileProvider = new RollingFileLoggerProvider(new LogFileOptions(directory), () => At);

            consoleProvider.CreateLogger("SteamAchievements.Core.App.SyncCoordinator")
                .LogDebug("sync started force={Force}", false);
            fileProvider.CreateLogger("SteamAchievements.Core.App.SyncCoordinator")
                .LogDebug("sync started force={Force}", false);

            var fileText = File.ReadAllText(Path.Combine(directory, "log.txt"));

            Assert.Equal(fileText, console.ToString());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void WritesEveryLevelBecauseNothingIsFiltered()
    {
        using var console = new StringWriter();
        using var provider = NewProvider(console);
        var log = provider.CreateLogger("X");

        log.LogTrace("t");
        log.LogDebug("d");
        log.LogInformation("i");
        log.LogWarning("w");
        log.LogError("e");
        log.LogCritical("c");

        Assert.Equal(6, console.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void ReportsEveryLevelAsEnabledExceptNone()
    {
        using var console = new StringWriter();
        using var provider = NewProvider(console);
        var log = provider.CreateLogger("X");

        Assert.True(log.IsEnabled(LogLevel.Trace));
        Assert.True(log.IsEnabled(LogLevel.Critical));
        Assert.False(log.IsEnabled(LogLevel.None));
    }

    [Fact]
    public void WritesNothingAfterTheProviderIsDisposed()
    {
        using var console = new StringWriter();
        var provider = NewProvider(console);
        provider.Dispose();

        provider.CreateLogger("X").LogInformation("too late");

        Assert.Equal(string.Empty, console.ToString());
    }
}
