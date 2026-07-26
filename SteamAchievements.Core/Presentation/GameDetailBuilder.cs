using System.Globalization;
using SteamAchievements.Core.Analytics;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Presentation;

public static class GameDetailBuilder
{
    private const string HiddenDescription = "Steam returns no description for hidden achievements";

    public static GameDetailView Build(
        OwnedGame game, IReadOnlyList<AchievementProgress> achievements, DateTimeOffset now)
    {
        var effort = EffortCalculator.Evaluate(achievements);

        // The same normalisation EffortCalculator uses, so the rarity bars and
        // the cost figures tell one story instead of two.
        var known = achievements.Where(a => a.GlobalPercent is > 0).ToList();
        var maxPercent = known.Count == 0 ? 0 : known.Max(a => a.GlobalPercent!.Value);

        var locked = achievements.Where(a => !a.Unlocked).ToList();

        var remaining = locked
            .OrderBy(a => Cost(a, maxPercent))
            .Select(a => Row(a, maxPercent))
            .ToList();

        var unlocked = achievements.Where(a => a.Unlocked)
            .OrderByDescending(a => a.UnlockedAt ?? DateTimeOffset.MinValue)
            .Select(a => Row(a, maxPercent))
            .ToList();

        var rarestPercent = locked
            .Where(a => a.GlobalPercent is not null)
            .Select(a => a.GlobalPercent!.Value)
            .DefaultIfEmpty(double.NaN)
            .Min();

        var complete = effort.TotalCount > 0 && effort.RemainingCount == 0;

        return new GameDetailView(
            game.AppId,
            game.Name,
            Formatting.Playtime(game.PlaytimeForever),
            game.LastPlayed is { } played ? Formatting.Relative(played, now) : "never",
            effort.UnlockedCount,
            effort.TotalCount,
            (int)Math.Round(effort.CompletionPercent),
            effort.RemainingEffort.ToString("0.#", CultureInfo.InvariantCulture),
            complete ? "complete" : QueueRowBuilder.EffortLabel(effort.RemainingEffort),
            effort.RemainingCount,
            double.IsNaN(rarestPercent) ? "unknown" : Formatting.Percent(rarestPercent),
            effort.RarityUnknown,
            remaining,
            unlocked);
    }

    private static double Cost(AchievementProgress achievement, double maxPercent) =>
        achievement.GlobalPercent is { } percent
            ? EffortCalculator.Cost(percent, maxPercent)
            : EffortCalculator.UnknownRarityCost;

    private static AchievementRow Row(AchievementProgress achievement, double maxPercent)
    {
        var percent = achievement.GlobalPercent;

        var bar = percent is { } value && maxPercent > 0
            ? Math.Clamp((int)Math.Round(100 * value / maxPercent), 0, 100)
            : 0;

        // Steam almost always returns an empty description for hidden
        // achievements, and there is nowhere to source one in the MVP. Saying
        // so is more useful than an empty line.
        var description = achievement.IsHidden && string.IsNullOrWhiteSpace(achievement.Description)
            ? HiddenDescription
            : achievement.Description;

        return new AchievementRow(
            achievement.DisplayName,
            description,
            achievement.IconUrl,
            achievement.IsHidden,
            percent,
            percent is { } p ? $"{Formatting.Percent(p)} of owners" : "rarity unknown",
            bar,
            Cost(achievement, maxPercent).ToString("0.0", CultureInfo.InvariantCulture),
            achievement.UnlockedAt is { } at ? Formatting.Date(at) : null);
    }
}
