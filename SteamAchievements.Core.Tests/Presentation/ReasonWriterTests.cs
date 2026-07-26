using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests.Presentation;

public class ReasonWriterTests
{
    private static AchievementProgress Locked(double? percent) =>
        new("api", "Name", "desc", "icon", false, false, null, percent);

    private static AchievementProgress Unlocked(double? percent, DateTimeOffset? at = null) =>
        new("api", "Name", "desc", "icon", false, true, at ?? DateTimeOffset.UnixEpoch, percent);

    [Fact]
    public void ReportsTheLastUnlockDateForAFinishedGame()
    {
        var reason = ReasonWriter.Write(
        [
            Unlocked(50, new DateTimeOffset(2025, 11, 11, 0, 0, 0, TimeSpan.Zero)),
            Unlocked(10, new DateTimeOffset(2026, 3, 24, 0, 0, 0, TimeSpan.Zero)),
        ]);

        Assert.Equal("Complete — last unlock 24 Mar 2026", reason);
    }

    [Fact]
    public void SaysOnlyCompleteWhenNoUnlockDateWasEverRecorded()
    {
        // Steam returns unlocktime 0 for achievements unlocked before it started
        // recording timestamps; the repository stores that as null.
        var reason = ReasonWriter.Write([new("api", "N", "d", "i", false, true, null, 50)]);

        Assert.Equal("Complete", reason);
    }

    [Fact]
    public void SaysRarityIsUnknownForAllOfThemWhenNoPercentagesExist()
    {
        var reason = ReasonWriter.Write(Enumerable.Range(0, 39).Select(_ => Locked(null)).ToList());

        Assert.Equal("39 left, rarity unknown for all of them", reason);
    }

    [Fact]
    public void CountsHowManyLackRarityWhenOnlySomeDo()
    {
        var achievements = Enumerable.Range(0, 33).Select(_ => Locked(20.0))
            .Concat(Enumerable.Range(0, 6).Select(_ => Locked(null)))
            .ToList();

        Assert.Equal("39 left, rarity unknown for 6 of them", ReasonWriter.Write(achievements));
    }

    [Fact]
    public void NamesThePercentageWhenExactlyOneRareAchievementIsLeftInAShortList()
    {
        var reason = ReasonWriter.Write([Locked(30.0), Locked(25.0), Locked(2.1)]);

        Assert.Equal("3 left: two common, one rare (2.1%)", reason);
    }

    [Fact]
    public void HandlesTheSingleRemainingRareAchievementWithoutASpuriousCommonClause()
    {
        Assert.Equal("1 left: one rare (2.1%)", ReasonWriter.Write([Locked(2.1)]));
    }

    [Fact]
    public void FallsBackToCountingWhenMoreThanFourAreLeftEvenWithASingleRareOne()
    {
        // The naming form only reads well for a short list; with five left the
        // sentence would be "5 left: four common, one rare (2.1%)", which is
        // more arithmetic than the reader asked for.
        var achievements = Enumerable.Range(0, 4).Select(_ => Locked(30.0))
            .Append(Locked(2.1)).ToList();

        Assert.Equal("5 left, one below 5% of owners", ReasonWriter.Write(achievements));
    }

    [Fact]
    public void CountsRareAchievementsWhenThereAreSeveral()
    {
        var achievements = Enumerable.Range(0, 12).Select(_ => Locked(30.0))
            .Concat(Enumerable.Range(0, 4).Select(_ => Locked(1.9)))
            .ToList();

        Assert.Equal("16 left, four below 5% of owners", ReasonWriter.Write(achievements));
    }

    [Fact]
    public void ReportsTheFloorOfTheLowestPercentageWhenNothingIsRare()
    {
        var achievements = new[] { Locked(8.4), Locked(12.0), Locked(30.0) }
            .Concat(Enumerable.Range(0, 3).Select(_ => Locked(40.0)))
            .ToList();

        Assert.Equal("6 left, all above 8% of owners", ReasonWriter.Write(achievements));
    }

    [Fact]
    public void TreatsExactlyFivePercentAsCommonNotRare()
    {
        // The threshold is "below 5%", so 5.0 itself must not trip it.
        Assert.Equal("2 left, all above 5% of owners",
            ReasonWriter.Write([Locked(5.0), Locked(9.0)]));

        Assert.Equal("2 left: one common, one rare (4.9%)",
            ReasonWriter.Write([Locked(4.9), Locked(9.0)]));
    }

    [Fact]
    public void IgnoresUnlockedAchievementsWhenDescribingWhatIsLeft()
    {
        var reason = ReasonWriter.Write([Unlocked(1.0), Locked(30.0), Locked(40.0)]);

        Assert.Equal("2 left, all above 30% of owners", reason);
    }
}
