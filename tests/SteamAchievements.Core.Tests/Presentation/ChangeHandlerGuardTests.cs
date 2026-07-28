using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests.Presentation;

public class ChangeHandlerGuardTests
{
    [Fact]
    public void RereadReturnsTrueAndLogsNothingWhenTheBodySucceeds()
    {
        var log = new RecordingLogger<ChangeHandlerGuardTests>();
        var ran = false;

        var result = ChangeHandlerGuard.Reread(log, "queue", () => ran = true);

        Assert.True(result);
        Assert.True(ran);
        Assert.Empty(log.Lines);
    }

    /// <summary>
    /// Code 5 is SQLITE_BUSY: the re-read raced a writer that had just
    /// finished. This is the expected, recoverable case, so it is a warning,
    /// not an error — and the caller is told not to re-render on stale data.
    /// </summary>
    [Fact]
    public void RereadLogsAWarningAndReturnsFalseOnSqliteBusy()
    {
        var log = new RecordingLogger<ChangeHandlerGuardTests>();

        var result = ChangeHandlerGuard.Reread(log, "queue", () => throw new SqliteException("locked", 5));

        Assert.False(result);
        Assert.Equal([LogLevel.Warning], log.Levels);
        Assert.True(log.Logged("queue re-read lost a race with the writer"));
    }

    /// <summary>
    /// Any other SqliteErrorCode is not the busy/locked race and must not be
    /// downgraded to a warning — it is a real failure hiding behind stale data.
    /// </summary>
    [Fact]
    public void RereadLogsAnErrorAndReturnsFalseOnAnyOtherSqliteErrorCode()
    {
        var log = new RecordingLogger<ChangeHandlerGuardTests>();

        var result = ChangeHandlerGuard.Reread(log, "queue", () => throw new SqliteException("corrupt", 11));

        Assert.False(result);
        Assert.Equal([LogLevel.Error], log.Levels);
        Assert.True(log.Logged("queue re-read failed"));
    }

    [Fact]
    public void RereadLogsAnErrorAndReturnsFalseOnAnyOtherException()
    {
        var log = new RecordingLogger<ChangeHandlerGuardTests>();

        var result = ChangeHandlerGuard.Reread(log, "summary", () => throw new InvalidOperationException("boom"));

        Assert.False(result);
        Assert.Equal([LogLevel.Error], log.Levels);
        Assert.True(log.Logged("summary re-read failed"));
    }

    [Fact]
    public void GuardReturnsTrueAndLogsNothingWhenTheBodySucceeds()
    {
        var log = new RecordingLogger<ChangeHandlerGuardTests>();
        var ran = false;

        var result = ChangeHandlerGuard.Guard(log, "reacting to a preference change failed", () => ran = true);

        Assert.True(result);
        Assert.True(ran);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void GuardLogsTheGivenMessageAsAnErrorAndReturnsFalseWhenTheBodyThrows()
    {
        var log = new RecordingLogger<ChangeHandlerGuardTests>();

        var result = ChangeHandlerGuard.Guard(
            log, "reacting to a preference change failed", () => throw new InvalidOperationException("boom"));

        Assert.False(result);
        Assert.Equal([LogLevel.Error], log.Levels);
        Assert.True(log.Logged("reacting to a preference change failed"));
    }
}
