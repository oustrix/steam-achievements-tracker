using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Presentation;

/// <summary>
/// Writes the one-line explanation of why a game sits where it does in the
/// queue. Without it a ranked list reads as guesswork.
///
/// The wording describes percentages and never achievability. "one rare
/// (2.1%)" reports that 2.1% of owners hold the achievement; it deliberately
/// does not say whether that is hard, dead, or worth attempting. See design
/// doc section 8.1 for why that distinction is load-bearing.
/// </summary>
public static class ReasonWriter
{
    /// <summary>
    /// An achievement is rare below this share of owners. One threshold, not
    /// several: the design mockup varies between 8%, 5%, 2% and 1% because it
    /// is hand-written prose, and a generator needs a single rule.
    /// </summary>
    public const double RareThreshold = 5.0;

    /// <summary>The naming form only reads well while the list is short.</summary>
    private const int NamedRarityLimit = 4;

    public static string Write(IReadOnlyList<AchievementProgress> achievements)
    {
        var locked = achievements.Where(a => !a.Unlocked).ToList();

        if (locked.Count == 0)
        {
            // Max over a nullable projection yields null for an empty sequence,
            // which is exactly the "no timestamps recorded" case.
            var last = achievements.Max(a => a.UnlockedAt);
            return last is null ? "Complete" : $"Complete — last unlock {Formatting.Date(last.Value)}";
        }

        // The leading count is always digits: it is the sentence's headline
        // number. Counts inside the clause use words up to nine.
        var head = $"{Formatting.Number(locked.Count)} left";

        var unknown = locked.Count(a => a.GlobalPercent is null);

        if (unknown == locked.Count)
        {
            return $"{head}, rarity unknown for all of them";
        }

        if (unknown > 0)
        {
            return $"{head}, rarity unknown for {Formatting.Count(unknown)} of them";
        }

        var rare = locked.Where(a => a.GlobalPercent!.Value < RareThreshold).ToList();

        if (rare.Count == 1 && locked.Count <= NamedRarityLimit)
        {
            var common = locked.Count - 1;
            var percent = Formatting.Percent(rare[0].GlobalPercent!.Value);

            return common == 0
                ? $"{head}: one rare ({percent})"
                : $"{head}: {Formatting.Count(common)} common, one rare ({percent})";
        }

        if (rare.Count > 0)
        {
            return $"{head}, {Formatting.Count(rare.Count)} below {RareThreshold:0}% of owners";
        }

        var lowest = locked.Min(a => a.GlobalPercent!.Value);
        return $"{head}, all above {Math.Floor(lowest):0}% of owners";
    }
}
