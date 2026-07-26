using SteamAchievements.Core.Analytics;
using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Tests.Analytics;

public class EffortCalculatorTests
{
    private static AchievementProgress Achievement(string name, bool unlocked, double? percent) =>
        new(name, name, string.Empty, string.Empty, false, unlocked, unlocked ? DateTimeOffset.UnixEpoch : null, percent);

    [Fact]
    public void MostCommonAchievementInAGameCostsNothing()
    {
        Assert.Equal(0, EffortCalculator.Cost(percent: 55, maxPercent: 55), 6);
    }

    [Fact]
    public void HalvingRelativeRarityAddsExactlyOne()
    {
        Assert.Equal(1, EffortCalculator.Cost(percent: 27.5, maxPercent: 55), 6);
        Assert.Equal(2, EffortCalculator.Cost(percent: 13.75, maxPercent: 55), 6);
    }

    [Fact]
    public void ZeroPercentIsClampedInsteadOfBecomingInfinite()
    {
        var cost = EffortCalculator.Cost(percent: 0, maxPercent: 55);

        Assert.True(double.IsFinite(cost));
        Assert.True(cost > 0);
    }

    [Fact]
    public void CountsOnlyLockedAchievementsTowardsEffort()
    {
        var effort = EffortCalculator.Evaluate(
        [
            Achievement("A", unlocked: true, percent: 50),
            Achievement("B", unlocked: false, percent: 25),
        ]);

        Assert.Equal(1, effort.RemainingCount);
        Assert.Equal(1, effort.UnlockedCount);
        Assert.Equal(2, effort.TotalCount);
        Assert.Equal(1, effort.RemainingEffort, 6);   // 25 / 50 → -log2(0.5) = 1
    }

    [Fact]
    public void ManyEasyAchievementsCostLessThanFewRareOnes()
    {
        var many = EffortCalculator.Evaluate(Enumerable.Range(0, 20)
            .Select(i => Achievement($"A{i}", unlocked: false, percent: i == 0 ? 60 : 55))
            .ToList());

        var few = EffortCalculator.Evaluate(
        [
            Achievement("Common", unlocked: true, percent: 60),
            Achievement("Rare1", unlocked: false, percent: 0.6),
            Achievement("Rare2", unlocked: false, percent: 0.6),
            Achievement("Rare3", unlocked: false, percent: 0.6),
        ]);

        Assert.True(many.RemainingEffort < few.RemainingEffort);
    }

    [Fact]
    public void FlagsBlockersBelowTwoPercentRelativeRarity()
    {
        var effort = EffortCalculator.Evaluate(
        [
            Achievement("Common", unlocked: true, percent: 50),
            Achievement("Dead", unlocked: false, percent: 0.4),   // 0.8% relative
        ]);

        Assert.True(effort.HasBlockers);
    }

    [Fact]
    public void DoesNotFlagBlockersThatAreAlreadyUnlocked()
    {
        var effort = EffortCalculator.Evaluate(
        [
            Achievement("Common", unlocked: true, percent: 50),
            Achievement("Rare", unlocked: true, percent: 0.4),
        ]);

        Assert.False(effort.HasBlockers);
    }

    [Fact]
    public void FallsBackToEqualWeightsWhenRarityIsUnknown()
    {
        var effort = EffortCalculator.Evaluate(
        [
            Achievement("A", unlocked: true, percent: null),
            Achievement("B", unlocked: false, percent: null),
            Achievement("C", unlocked: false, percent: null),
        ]);

        Assert.True(effort.RarityUnknown);
        Assert.Equal(2, effort.RemainingEffort, 6);   // one unit per locked achievement
        Assert.False(effort.HasBlockers);
    }

    [Fact]
    public void ReportsCompletionPercent()
    {
        var effort = EffortCalculator.Evaluate(
        [
            Achievement("A", unlocked: true, percent: 50),
            Achievement("B", unlocked: true, percent: 40),
            Achievement("C", unlocked: false, percent: 30),
            Achievement("D", unlocked: false, percent: 20),
        ]);

        Assert.Equal(50, effort.CompletionPercent, 6);
    }

    [Fact]
    public void HandlesGameWithNoAchievements()
    {
        var effort = EffortCalculator.Evaluate([]);

        Assert.Equal(0, effort.TotalCount);
        Assert.Equal(0, effort.RemainingEffort);
        Assert.Equal(0, effort.CompletionPercent);
    }

    [Fact]
    public void HandlesFullyCompletedGame()
    {
        var effort = EffortCalculator.Evaluate(
        [
            Achievement("A", unlocked: true, percent: 50),
            Achievement("B", unlocked: true, percent: 10),
        ]);

        Assert.Equal(0, effort.RemainingEffort);
        Assert.Equal(100, effort.CompletionPercent, 6);
    }
}
