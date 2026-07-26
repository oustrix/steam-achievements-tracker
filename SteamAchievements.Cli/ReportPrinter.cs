using SteamAchievements.Core.Analytics;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Cli;

/// <summary>
/// Renders the post-sync ranking: the payoff of the whole tool. Ranking logic
/// itself (EffortCalculator) is untouched — this only formats its output.
/// </summary>
public static class ReportPrinter
{
    private const int NameColumnWidth = 32;

    public static void Print(GameRepository repository, int top, int requestCount, TimeSpan elapsed)
    {
        var owned = repository.GetOwnedGames();
        var states = repository.GetSyncStates();

        var ranked = new List<(OwnedGame Game, GameEffort Effort, string Why)>();
        var gamesWithAchievements = 0;

        foreach (var game in owned)
        {
            var progress = repository.GetGameProgress(game.AppId);
            if (progress.Count == 0)
            {
                continue;
            }

            gamesWithAchievements++;
            var effort = EffortCalculator.Evaluate(progress);
            if (effort.TotalCount == 0)
            {
                continue;
            }

            ranked.Add((game, effort, DescribeRemaining(progress, effort)));
        }

        var topRows = ranked
            .Where(r => r.Effort.RemainingCount > 0)
            .OrderBy(r => r.Effort.RemainingEffort)
            .Take(top)
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"Top {topRows.Count} games by remaining effort:");
        Console.WriteLine();

        var header = string.Format(
            "{0,4}  {1,-" + NameColumnWidth + "}  {2,-11}  {3,6}  {4,8}  {5}",
            "#", "Game", "Progress", "Pct", "Effort", "Why");
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length + 20));

        for (var i = 0; i < topRows.Count; i++)
        {
            var (game, effort, why) = topRows[i];
            var progressText = $"{effort.UnlockedCount}/{effort.TotalCount}";
            Console.WriteLine(string.Format(
                "{0,4}  {1,-" + NameColumnWidth + "}  {2,-11}  {3,5:F1}%  {4,8:F2}  {5}",
                i + 1, Truncate(game.Name, NameColumnWidth), progressText, effort.CompletionPercent, effort.RemainingEffort, why));
        }

        var skippedNoAchievements = states.Values.Count(s => !s.HasAchievements);

        Console.WriteLine();
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Total games:                {owned.Count}");
        Console.WriteLine($"  Games with achievements:    {gamesWithAchievements}");
        Console.WriteLine($"  Skipped (no achievements):  {skippedNoAchievements}");
        Console.WriteLine($"  Total HTTP requests:        {requestCount}");
        Console.WriteLine($"  Wall-clock duration:        {elapsed:hh\\:mm\\:ss}");
    }

    private static string DescribeRemaining(IReadOnlyList<AchievementProgress> progress, GameEffort effort)
    {
        if (effort.RemainingCount == 0)
        {
            return "complete";
        }

        if (effort.RarityUnknown)
        {
            return $"{effort.RemainingCount} left, rarity unknown";
        }

        var rarest = progress
            .Where(p => !p.Unlocked && p.GlobalPercent is > 0)
            .OrderBy(p => p.GlobalPercent)
            .FirstOrDefault();

        return rarest is null
            ? $"{effort.RemainingCount} left"
            : $"{effort.RemainingCount} left, rarest {rarest.GlobalPercent:F1}%";
    }

    private static string Truncate(string name, int width)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "(unnamed)";
        }

        return name.Length <= width ? name : name[..(width - 1)] + "…";
    }
}
