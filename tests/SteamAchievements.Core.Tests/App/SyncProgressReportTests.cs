using SteamAchievements.Core.App;

namespace SteamAchievements.Core.Tests.App;

public class SyncProgressReportTests
{
    [Fact]
    public void ReportsTheAverageRateSoFar()
    {
        Assert.Equal("5.0 games/s", SyncProgressReport.Rate(50, TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(50, 0)]
    public void SaysNothingAboutRateBeforeThereIsSomethingToDivide(int completed, int elapsedSeconds)
    {
        Assert.Equal(
            string.Empty,
            SyncProgressReport.Rate(completed, TimeSpan.FromSeconds(elapsedSeconds)));
    }

    [Fact]
    public void ExtrapolatesTheRemainingTimeFromTheAverage()
    {
        // 100 of 1000 games in 60 s means 0.6 s per game, so 900 left is 9 min.
        Assert.Equal("~9 min left", SyncProgressReport.Eta(100, 1000, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void RoundsUpToAMinuteRatherThanPromisingSeconds()
    {
        Assert.Equal("~1 min left", SyncProgressReport.Eta(999, 1000, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void SwitchesToHoursForLongRuns()
    {
        // 10 of 1000 in 60 s is 6 s per game: 990 left is 99 minutes.
        Assert.Equal("~1.7 h left", SyncProgressReport.Eta(10, 1000, TimeSpan.FromSeconds(60)));
    }

    [Theory]
    [InlineData(0, 100, 60)]
    [InlineData(100, 100, 60)]
    [InlineData(150, 100, 60)]
    [InlineData(10, 100, 0)]
    public void SaysNothingWhenThereIsNothingLeftToEstimate(int completed, int total, int elapsedSeconds)
    {
        Assert.Equal(
            string.Empty,
            SyncProgressReport.Eta(completed, total, TimeSpan.FromSeconds(elapsedSeconds)));
    }
}
