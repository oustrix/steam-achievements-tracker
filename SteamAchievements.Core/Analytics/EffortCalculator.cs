using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Analytics;

public sealed record GameEffort(
    double RemainingEffort,
    int RemainingCount,
    int UnlockedCount,
    int TotalCount,
    bool RarityUnknown,
    double CompletionPercent);

/// <summary>
/// Ranks games by how much work is left rather than by completion percentage.
///
/// Steam computes global achievement percentages across everyone who owns a
/// game, including people who never launched it, so raw percentages are not
/// comparable between titles. Normalizing against the game's own most common
/// achievement removes that distortion.
///
/// Deliberately does not attempt to flag "blocker" or "dead" achievements: a
/// low global percentage does not reliably mean an achievement is hard or
/// unobtainable. It conflates unrelated causes — achievements added years
/// after release for a game most owners had already stopped playing,
/// conditions that are rare in normal play but easy once pursued
/// deliberately, and only occasionally a genuinely dead multiplayer mode or
/// broken trigger. Presenting that noisy heuristic as a fact ("this game is
/// questionable") risks someone abandoning a game they would have finished.
/// </summary>
public static class EffortCalculator
{
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
            return new GameEffort(0, 0, 0, 0, false, 0);
        }

        var unlocked = achievements.Count(a => a.Unlocked);
        var locked = achievements.Where(a => !a.Unlocked).ToList();
        var completion = 100.0 * unlocked / achievements.Count;

        var known = achievements.Where(a => a.GlobalPercent is > 0).ToList();

        if (known.Count == 0)
        {
            // No rarity data at all — every remaining achievement counts as one unit.
            return new GameEffort(locked.Count, locked.Count, unlocked, achievements.Count,
                RarityUnknown: true, completion);
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

        foreach (var achievement in locked)
        {
            if (achievement.GlobalPercent is not { } percent)
            {
                // Unknown rarity for this achievement specifically. Give it the
                // same neutral weight as the whole-game fallback rather than
                // treating "we don't know" as a claim about rarity.
                effort += UnknownRarityCost;
                continue;
            }

            effort += Cost(percent, maxPercent);
        }

        return new GameEffort(effort, locked.Count, unlocked, achievements.Count,
            RarityUnknown: false, completion);
    }
}
