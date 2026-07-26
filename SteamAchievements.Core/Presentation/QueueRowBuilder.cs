using System.Globalization;
using SteamAchievements.Core.Analytics;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Presentation;

public static class QueueRowBuilder
{
    /// <summary>Smallest visible bar, so a nearly-finished game still reads as started.</summary>
    private const int MinimumBarPercent = 4;

    public static QueueRow Build(OwnedGame game, IReadOnlyList<AchievementProgress> achievements)
    {
        var effort = EffortCalculator.Evaluate(achievements);
        var complete = effort.TotalCount > 0 && effort.RemainingCount == 0;

        return new QueueRow(
            game.AppId,
            game.Name,
            effort.UnlockedCount,
            effort.TotalCount,
            (int)Math.Round(effort.CompletionPercent),
            effort.RemainingEffort,
            effort.RemainingEffort.ToString("0.#", CultureInfo.InvariantCulture),
            complete ? "complete" : EffortLabel(effort.RemainingEffort),
            ReasonWriter.Write(achievements),
            game.PlaytimeForever / 60,
            complete,
            effort.RarityUnknown);
    }

    /// <summary>
    /// Turns an abstract effort number into something a person can plan around.
    /// The thresholds come from the design mockup.
    /// </summary>
    public static string EffortLabel(double effort) => effort switch
    {
        < 8 => "an evening",
        < 25 => "a few sessions",
        < 80 => "a long haul",
        _ => "a project",
    };

    /// <summary>
    /// Bar width on a logarithmic scale against the largest effort currently
    /// on screen. Linear scaling would collapse everything below the biggest
    /// game in the library into an invisible sliver — Europa Universalis IV
    /// carries 342 units against Hollow Knight's 4.2.
    /// </summary>
    public static int EffortBarPercent(double effort, double maxEffort)
    {
        if (effort <= 0 || maxEffort <= 0)
        {
            return 0;
        }

        var scaled = 100 * Math.Log(1 + effort) / Math.Log(1 + maxEffort);
        return Math.Max(MinimumBarPercent, (int)Math.Round(scaled));
    }
}
