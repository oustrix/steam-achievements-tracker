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

        // effort.MaxPercent is the baseline EffortCalculator normalised against,
        // so the rarity bars and the cost figures tell one story instead of two.
        var maxPercent = effort.MaxPercent;

        var locked = achievements.Where(a => !a.Unlocked).ToList();

        var remaining = locked
            .Select(a => (Achievement: a, Cost: EffortCalculator.CostOf(a, maxPercent)))
            .OrderBy(entry => entry.Cost)
            .Select(entry => Row(entry.Achievement, maxPercent, entry.Cost))
            .ToList();

        var unlocked = achievements.Where(a => a.Unlocked)
            .OrderByDescending(a => a.UnlockedAt ?? DateTimeOffset.MinValue)
            .Select(a => Row(a, maxPercent, EffortCalculator.CostOf(a, maxPercent)))
            .ToList();

        // Min over double? ignores the unknowns and yields null when every
        // locked achievement lacks a percent — no sentinel value needed.
        var rarestPercent = locked.Select(a => a.GlobalPercent).Min();

        return new GameDetailView(
            game.AppId,
            game.Name,
            Formatting.Playtime(game.PlaytimeForever),
            game.LastPlayed is { } played ? Formatting.Relative(played, now) : "never",
            effort.UnlockedCount,
            effort.TotalCount,
            (int)Math.Round(effort.CompletionPercent),
            effort.RemainingEffort.ToString("0.#", CultureInfo.InvariantCulture),
            effort.Complete ? "complete" : QueueRowBuilder.EffortLabel(effort.RemainingEffort),
            effort.RemainingCount,
            rarestPercent is { } rarest ? Formatting.Percent(rarest) : "unknown",
            effort.RarityUnknown,
            remaining,
            unlocked);
    }

    private static AchievementRow Row(
        AchievementProgress achievement, double maxPercent, double cost)
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
            cost.ToString("0.0", CultureInfo.InvariantCulture),
            achievement.UnlockedAt is { } at ? Formatting.Date(at) : null);
    }
}
