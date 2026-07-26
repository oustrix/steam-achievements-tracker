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
    public void AGameWithNothingLeftIsComplete()
    {
        var effort = EffortCalculator.Evaluate(
        [
            Achievement("A", unlocked: true, percent: 50),
            Achievement("B", unlocked: true, percent: 25),
        ]);

        Assert.True(effort.Complete);
    }

    [Fact]
    public void AGameWithNoAchievementsAtAllIsNotComplete()
    {
        // Zero remaining out of zero is not an accomplishment. Both screens read
        // Complete straight off this record, so the distinction has to hold here
        // rather than in each of them.
        var effort = EffortCalculator.Evaluate([]);

        Assert.Equal(0, effort.RemainingCount);
        Assert.False(effort.Complete);
    }

    [Fact]
    public void PublishesTheBaselineItNormalisedAgainst()
    {
        // The game screen prices individual achievements against this number, so
        // it has to be the same one the total above the list was summed from.
        var effort = EffortCalculator.Evaluate(
        [
            Achievement("A", unlocked: true, percent: 55),
            Achievement("B", unlocked: false, percent: 27.5),
        ]);

        Assert.Equal(55, effort.MaxPercent, 6);
        Assert.Equal(effort.RemainingEffort, EffortCalculator.CostOf(
            Achievement("B", unlocked: false, percent: 27.5), effort.MaxPercent), 6);
    }

    [Fact]
    public void PricesAnAchievementWithNoRarityAtTheNeutralWeight()
    {
        var unknown = EffortCalculator.CostOf(
            Achievement("A", unlocked: false, percent: null), maxPercent: 55);

        Assert.Equal(EffortCalculator.UnknownRarityCost, unknown, 6);
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

    [Fact]
    public void UnknownRarityAchievementsGetNeutralWeightNotMaximalRarity()
    {
        var effort = EffortCalculator.Evaluate(
        [
            Achievement("Common", unlocked: true, percent: 80),
            Achievement("Unknown", unlocked: false, percent: null),
        ]);

        // If null were still conflated with zero this would cost roughly
        // -log2(0.001) ≈ 9.97 (the rarity floor) instead of a neutral 1 unit.
        Assert.Equal(1, effort.RemainingEffort, 6);
    }

    [Fact]
    public void MixedKnownAndUnknownEffortSitsBetweenFullyKnownAndFullyUnknownCases()
    {
        var fullyUnknown = EffortCalculator.Evaluate(
        [
            Achievement("Common", unlocked: true, percent: null),
            Achievement("Locked", unlocked: false, percent: null),
        ]);

        var mixed = EffortCalculator.Evaluate(
        [
            Achievement("Common", unlocked: true, percent: 80),
            Achievement("Locked", unlocked: false, percent: null),
        ]);

        // Fully unknown falls back to exactly one unit per locked achievement.
        Assert.Equal(1, fullyUnknown.RemainingEffort, 6);

        // A single unknown achievement in an otherwise-known game gets the same
        // neutral unit, not something inflated by treating it as a verified zero
        // (which would push it up towards the ~9.97 rarity-floor cost).
        Assert.Equal(fullyUnknown.RemainingEffort, mixed.RemainingEffort, 6);
        Assert.True(mixed.RemainingEffort < 5);
    }
}
