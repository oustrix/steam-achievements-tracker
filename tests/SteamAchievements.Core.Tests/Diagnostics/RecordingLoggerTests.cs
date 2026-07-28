using Microsoft.Extensions.Logging;

namespace SteamAchievements.Core.Tests.Diagnostics;

public class RecordingLoggerTests
{
    [Fact]
    public void RecordsTheFormattedMessageOfEveryCall()
    {
        var log = new RecordingLogger<RecordingLoggerTests>();

        log.LogInformation("plan has {Count} games", 1483);

        Assert.Equal(["plan has 1483 games"], log.Lines);
    }

    [Fact]
    public void RecordsTheExceptionAlongsideTheMessage()
    {
        var log = new RecordingLogger<RecordingLoggerTests>();
        var boom = new InvalidOperationException("boom");

        log.LogError(boom, "it failed");

        Assert.Same(boom, log.Errors.Single());
    }

    [Fact]
    public void AnswersWhetherAFragmentWasLogged()
    {
        var log = new RecordingLogger<RecordingLoggerTests>();

        log.LogDebug("sync completed in 512ms");

        Assert.True(log.Logged("sync completed"));
        Assert.False(log.Logged("sync failed"));
    }

    [Fact]
    public void IsSafeToWriteToFromSeveralThreads()
    {
        // SyncOrchestrator logs from four worker threads.
        var log = new RecordingLogger<RecordingLoggerTests>();

        Parallel.For(0, 200, n => log.LogDebug("line {N}", n));

        Assert.Equal(200, log.Lines.Count);
    }
}
