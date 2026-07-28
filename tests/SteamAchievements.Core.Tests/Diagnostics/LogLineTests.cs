using Microsoft.Extensions.Logging;
using SteamAchievements.Core.Diagnostics;

namespace SteamAchievements.Core.Tests.Diagnostics;

public class LogLineTests
{
    private static readonly DateTimeOffset At =
        new(2026, 7, 27, 9, 14, 2, 113, TimeSpan.Zero);

    [Fact]
    public void WritesASortableUtcTimestampALevelACategoryAndTheMessage()
    {
        Assert.Equal(
            "2026-07-27 09:14:02.113Z  DBG  SyncCoordinator  sync started force=False\r\n",
            LogLine.Format(At, LogLevel.Debug, "SteamAchievements.Core.App.SyncCoordinator",
                "sync started force=False", null));
    }

    [Fact]
    public void ConvertsANonUtcTimestampRatherThanPrintingItsLocalFace()
    {
        // Two hours ahead of the same instant above.
        var offset = new DateTimeOffset(2026, 7, 27, 11, 14, 2, 113, TimeSpan.FromHours(2));

        Assert.StartsWith("2026-07-27 09:14:02.113Z", LogLine.Format(offset, LogLevel.Debug, "X", "m", null));
    }

    [Theory]
    [InlineData(LogLevel.Trace, "TRC")]
    [InlineData(LogLevel.Debug, "DBG")]
    [InlineData(LogLevel.Information, "INF")]
    [InlineData(LogLevel.Warning, "WRN")]
    [InlineData(LogLevel.Error, "ERR")]
    [InlineData(LogLevel.Critical, "CRT")]
    public void AbbreviatesEveryLevelToThreeCharacters(LogLevel level, string expected)
    {
        Assert.Equal($"2026-07-27 09:14:02.113Z  {expected}  X  m\r\n",
            LogLine.Format(At, level, "X", "m", null));
    }

    [Fact]
    public void AppendsAnExceptionAsAnIndentedBlockAfterItsLine()
    {
        Exception caught;

        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception e)
        {
            caught = e;
        }

        var formatted = LogLine.Format(At, LogLevel.Error, "X", "it failed", caught);
        var lines = formatted.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("2026-07-27 09:14:02.113Z  ERR  X  it failed", lines[0]);
        Assert.StartsWith("    System.InvalidOperationException: boom", lines[1]);
        Assert.All(lines.Skip(1), line => Assert.StartsWith("    ", line));
        Assert.EndsWith("\r\n", formatted);
    }

    [Fact]
    public void ShortensANamespacedCategoryToItsLastSegment()
    {
        Assert.Equal("SyncCoordinator", LogLine.ShortCategory("SteamAchievements.Core.App.SyncCoordinator"));
    }

    [Fact]
    public void LeavesACategoryWithNoNamespaceAlone()
    {
        Assert.Equal("Program", LogLine.ShortCategory("Program"));
    }
}
