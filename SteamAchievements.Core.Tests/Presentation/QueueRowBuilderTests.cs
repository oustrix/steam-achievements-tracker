using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Presentation;

public class QueueRowBuilderTests
{
    private static OwnedGame Game(int playtimeMinutes = 5040) =>
        new(367520, "Hollow Knight", "hash", playtimeMinutes, 0,
            new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero));

    private static AchievementProgress Locked(double? percent) =>
        new("api", "Name", "desc", "icon", false, false, null, percent);

    private static AchievementProgress Unlocked(double percent) =>
        new("api", "Name", "desc", "icon", false, true, DateTimeOffset.UnixEpoch, percent);

    [Theory]
    [InlineData(0.0, "an evening")]
    [InlineData(7.9, "an evening")]
    [InlineData(8.0, "a few sessions")]
    [InlineData(24.9, "a few sessions")]
    [InlineData(25.0, "a long haul")]
    [InlineData(79.9, "a long haul")]
    [InlineData(80.0, "a project")]
    [InlineData(342.9, "a project")]
    public void LabelsEffortInHumanTerms(double effort, string expected)
    {
        Assert.Equal(expected, QueueRowBuilder.EffortLabel(effort));
    }

    [Fact]
    public void ScalesTheEffortBarLogarithmicallySoLargeGamesDoNotFlattenSmallOnes()
    {
        // Linear scaling against Europa Universalis IV's 342 units would render
        // a 4.2-unit game at 1% — indistinguishable from empty.
        var small = QueueRowBuilder.EffortBarPercent(4.2, 342.9);
        var large = QueueRowBuilder.EffortBarPercent(342.9, 342.9);

        Assert.Equal(100, large);
        Assert.InRange(small, 20, 35);
    }

    [Fact]
    public void NeverRendersANonZeroEffortAsAnEmptyTrack()
    {
        Assert.Equal(4, QueueRowBuilder.EffortBarPercent(0.01, 342.9));
    }

    [Fact]
    public void GivesACompletedGameNoBarAtAll()
    {
        Assert.Equal(0, QueueRowBuilder.EffortBarPercent(0, 342.9));
    }

    [Fact]
    public void SurvivesAListWhereEveryGameHasZeroEffort()
    {
        Assert.Equal(0, QueueRowBuilder.EffortBarPercent(0, 0));
    }

    [Fact]
    public void BuildsARowFromAGameAndItsAchievements()
    {
        var row = QueueRowBuilder.Build(Game(), [Unlocked(60), Unlocked(50), Locked(2.1)]);

        Assert.Equal(367520u, row.AppId);
        Assert.Equal("Hollow Knight", row.Name);
        Assert.Equal(2, row.Unlocked);
        Assert.Equal(3, row.Total);
        Assert.Equal(67, row.CompletionPercent);
        Assert.Equal(84, row.PlaytimeHours);
        Assert.False(row.Complete);
        Assert.False(row.RarityUnknown);
        Assert.Equal("1 left: one rare (2.1%)", row.Reason);
    }

    [Fact]
    public void MarksAFullyUnlockedGameCompleteAndLabelsItSo()
    {
        var row = QueueRowBuilder.Build(Game(), [Unlocked(60), Unlocked(50)]);

        Assert.True(row.Complete);
        Assert.Equal(100, row.CompletionPercent);
        Assert.Equal("0", row.EffortText);
        Assert.Equal("complete", row.EffortLabel);
    }

    [Fact]
    public void FormatsEffortWithAtMostOneDecimal()
    {
        var row = QueueRowBuilder.Build(Game(), [Unlocked(80), Locked(20), Locked(20)]);

        // Two locked at 20% against a 80% maximum: -log2(0.25) = 2 each.
        Assert.Equal("4", row.EffortText);
    }

    [Fact]
    public void PropagatesTheUnknownRarityFlagFromTheCalculator()
    {
        var row = QueueRowBuilder.Build(Game(), [Locked(null), Locked(null)]);

        Assert.True(row.RarityUnknown);
    }
}
