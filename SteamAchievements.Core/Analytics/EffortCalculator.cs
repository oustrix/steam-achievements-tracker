using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Analytics;

public sealed record GameEffort(
    double RemainingEffort,
    int RemainingCount,
    int UnlockedCount,
    int TotalCount,
    bool HasBlockers,
    bool RarityUnknown,
    double CompletionPercent);

/// <summary>
/// Ranks games by how much work is left rather than by completion percentage.
///
/// Steam computes global achievement percentages across everyone who owns a
/// game, including people who never launched it, so raw percentages are not
/// comparable between titles. Normalizing against the game's own most common
/// achievement removes that distortion.
/// </summary>
public static class EffortCalculator
{
    /// <summary>Relative rarity below this marks an achievement as a blocker.</summary>
    private const double BlockerThreshold = 0.02;

    /// <summary>Floor for relative rarity; without it a 0% achievement yields infinity.</summary>
    private const double RarityFloor = 0.001;

    public static double Cost(double percent, double maxPercent)
    {
        if (maxPercent <= 0)
        {
            return 1;
        }

        var relative = Math.Max(percent / maxPercent, RarityFloor);
        return -Math.Log2(Math.Min(relative, 1));
    }

    public static GameEffort Evaluate(IReadOnlyList<AchievementProgress> achievements)
    {
        if (achievements.Count == 0)
        {
            return new GameEffort(0, 0, 0, 0, false, false, 0);
        }

        var unlocked = achievements.Count(a => a.Unlocked);
        var locked = achievements.Where(a => !a.Unlocked).ToList();
        var completion = 100.0 * unlocked / achievements.Count;

        var known = achievements.Where(a => a.GlobalPercent is > 0).ToList();

        if (known.Count == 0)
        {
            // No rarity data at all — every remaining achievement counts as one unit.
            return new GameEffort(locked.Count, locked.Count, unlocked, achievements.Count,
                HasBlockers: false, RarityUnknown: true, completion);
        }

        var maxPercent = known.Max(a => a.GlobalPercent!.Value);
        var effort = 0.0;
        var hasBlockers = false;

        foreach (var achievement in locked)
        {
            var percent = achievement.GlobalPercent ?? 0;
            effort += Cost(percent, maxPercent);

            if (percent / maxPercent < BlockerThreshold)
            {
                hasBlockers = true;
            }
        }

        return new GameEffort(effort, locked.Count, unlocked, achievements.Count,
            hasBlockers, RarityUnknown: false, completion);
    }
}
