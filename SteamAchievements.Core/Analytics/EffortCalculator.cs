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

    /// <summary>
    /// Absolute global percent below this marks an achievement as a blocker,
    /// regardless of its standing relative to the rest of the game. The relative
    /// rule alone cannot see this: when the game's own most common remaining
    /// achievement is itself vanishingly rare in absolute terms, its relative
    /// rarity is always 1.0, so it would otherwise never be flagged.
    /// </summary>
    private const double AbsoluteBlockerPercent = 1.0;

    /// <summary>Floor for relative rarity; without it a 0% achievement yields infinity.</summary>
    private const double RarityFloor = 0.001;

    /// <summary>
    /// Cost assigned to a locked achievement whose global percent is unknown.
    /// Matches the equal-weight treatment used when a whole game has no rarity
    /// data at all — an unknown percent must never be treated as a verified
    /// zero, which would wrongly claim maximal rarity.
    /// </summary>
    private const double UnknownRarityCost = 1;

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

        // RarityUnknown reports "we have no basis to rank this game at all," so it
        // only applies when every achievement lacks data (above). A game with a
        // partial mix still has a real, usable baseline from its known achievements;
        // the individual unknowns are handled below with neutral weight instead of
        // downgrading the whole game to unranked. Flagging RarityUnknown for any
        // single missing datum would make it true for most recently-updated games
        // (Steam frequently lags on backfilling stats for new achievements),
        // making the signal nearly useless.

        var maxPercent = known.Max(a => a.GlobalPercent!.Value);
        var effort = 0.0;
        var hasBlockers = false;

        foreach (var achievement in locked)
        {
            if (achievement.GlobalPercent is not { } percent)
            {
                // Unknown rarity for this achievement specifically. Give it the
                // same neutral weight as the whole-game fallback, and never treat
                // "we don't know" as a claim about rarity — a blocker is a claim
                // this achievement is confirmed rare, which absent data isn't true.
                effort += UnknownRarityCost;
                continue;
            }

            effort += Cost(percent, maxPercent);

            var isRelativeBlocker = percent / maxPercent < BlockerThreshold;
            var isAbsoluteBlocker = percent < AbsoluteBlockerPercent;
            if (isRelativeBlocker || isAbsoluteBlocker)
            {
                hasBlockers = true;
            }
        }

        return new GameEffort(effort, locked.Count, unlocked, achievements.Count,
            hasBlockers, RarityUnknown: false, completion);
    }
}
