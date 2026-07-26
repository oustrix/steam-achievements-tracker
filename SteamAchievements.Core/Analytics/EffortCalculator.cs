using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Analytics;

public sealed record GameEffort(
    double RemainingEffort,
    int RemainingCount,
    int UnlockedCount,
    int TotalCount,
    bool RarityUnknown,
    double CompletionPercent,
    double MaxPercent)
{
    /// <summary>
    /// Nothing left to unlock. A game with no achievements at all is not
    /// complete — there was never anything to finish — which is why this is
    /// not simply <c>RemainingCount == 0</c>.
    /// </summary>
    public bool Complete => TotalCount > 0 && RemainingCount == 0;
}

/// <summary>
/// Ranks games by how much work is left rather than by completion percentage.
///
/// Steam computes global achievement percentages across everyone who owns a
/// game, including people who never launched it, so raw percentages are not
/// comparable between titles. Normalizing against the game's own most common
/// achievement removes that distortion — but normalizing alone collapses when
/// a game's achievements are all similarly rare (see <see cref="AbsoluteRarityWeight"/>):
/// a title nobody has played, whose every achievement sits at 2% globally, reads
/// as "every achievement is average for this game" and costs almost nothing. The
/// per-achievement cost therefore blends the relative (in-game) rarity with the
/// achievement's absolute (global) rarity, so a uniformly-rare game still costs
/// something even though no single achievement stands out from its peers.
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
    /// <summary>Floor for rarity ratios; without it a 0% achievement yields infinity.</summary>
    private const double RarityFloor = 0.001;

    /// <summary>
    /// Weight given to the absolute (global) rarity term relative to the
    /// in-game relative term. The relative term is what makes costs
    /// comparable across games of wildly different achievement counts and
    /// audience sizes — it stays the dominant term. The absolute term exists
    /// only to stop a game whose achievements are all uniformly rare (e.g.
    /// Overture: 4 achievements at 2.1%-2.2%, or Rust - Staging Branch: 6 at
    /// 3.7%-3.8%) from looking "free" just because nothing in that narrow
    /// band stands out from its peers. Half weight is enough to fix that
    /// failure mode without letting a single very rare achievement in an
    /// otherwise-reasonable game dominate the score the way a 1.0 weight
    /// would.
    /// </summary>
    private const double AbsoluteRarityWeight = 0.5;

    /// <summary>
    /// Cost assigned to a locked achievement whose global percent is unknown.
    /// Matches the equal-weight treatment used when a whole game has no rarity
    /// data at all — an unknown percent must never be treated as a verified
    /// zero, which would wrongly claim maximal rarity.
    ///
    /// Public because the game screen orders individual achievements by the
    /// same cost and must agree with the total shown above the list.
    /// </summary>
    public const double UnknownRarityCost = 1;

    /// <summary>
    /// What one locked achievement contributes to <see cref="GameEffort.RemainingEffort"/>,
    /// including the neutral weight an unknown percent receives.
    ///
    /// The game screen orders achievements by this number and prints it beside
    /// each one, so it has to be the same arithmetic the total above the list
    /// was summed from. Exposing the operation rather than only its constants
    /// keeps the two in agreement by construction, instead of by a test that
    /// notices once they have already drifted.
    /// </summary>
    public static double CostOf(AchievementProgress achievement, double maxPercent) =>
        achievement.GlobalPercent is { } percent
            ? Cost(percent, maxPercent)
            : UnknownRarityCost;

    public static double Cost(double percent, double maxPercent)
    {
        if (maxPercent <= 0)
        {
            return 1;
        }

        var relative = Clamp01(percent / maxPercent);
        var absolute = Clamp01(percent / 100);

        return -Math.Log2(relative) + AbsoluteRarityWeight * -Math.Log2(absolute);
    }

    /// <summary>Clamps a ratio into (RarityFloor, 1], keeping -log2 finite and non-negative.</summary>
    private static double Clamp01(double ratio) => Math.Min(Math.Max(ratio, RarityFloor), 1);

    public static GameEffort Evaluate(IReadOnlyList<AchievementProgress> achievements)
    {
        if (achievements.Count == 0)
        {
            return new GameEffort(0, 0, 0, 0, false, 0, 0);
        }

        var unlocked = achievements.Count(a => a.Unlocked);
        var locked = achievements.Where(a => !a.Unlocked).ToList();
        var completion = 100.0 * unlocked / achievements.Count;

        var known = achievements.Where(a => a.GlobalPercent is > 0).ToList();

        if (known.Count == 0)
        {
            // No rarity data at all — every remaining achievement counts as one unit.
            return new GameEffort(locked.Count, locked.Count, unlocked, achievements.Count,
                RarityUnknown: true, completion, MaxPercent: 0);
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

        // CostOf gives an achievement with no percent the same neutral weight as
        // the whole-game fallback above, rather than treating "we don't know" as
        // a claim about rarity.
        var effort = locked.Sum(a => CostOf(a, maxPercent));

        return new GameEffort(effort, locked.Count, unlocked, achievements.Count,
            RarityUnknown: false, completion, maxPercent);
    }
}
