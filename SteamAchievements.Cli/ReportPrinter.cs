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

        // "What should I complete next" and "what should I start" are different
        // questions with different answers, so they get separate lists rather
        // than one queue with a filter flag. A game with 0% progress is not a
        // suggestion to finish something — it is a suggestion to start
        // something new, and burying 13 of those in the top 20 of a single
        // list (as the live run did) makes the "finish" list useless.
        var inProgress = ranked.Where(r => r.Effort.RemainingCount > 0 && r.Effort.UnlockedCount > 0)
            .OrderBy(r => r.Effort.RemainingEffort)
            .ToList();
        var notStarted = ranked.Where(r => r.Effort.RemainingCount > 0 && r.Effort.UnlockedCount == 0)
            .OrderBy(r => r.Effort.RemainingEffort)
            .ToList();
        var fullyCompleted = ranked.Count(r => r.Effort.RemainingCount == 0);

        PrintTable("Finish what you started", inProgress.Take(top).ToList());
        PrintTable("Start something new", notStarted.Take(top).ToList());

        var skippedNoAchievements = states.Values.Count(s => !s.HasAchievements);

        Console.WriteLine();
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Total games:                {owned.Count}");
        Console.WriteLine($"  Games with achievements:    {gamesWithAchievements}");
        Console.WriteLine($"  In progress:                {inProgress.Count}");
        Console.WriteLine($"  Not started:                {notStarted.Count}");
        Console.WriteLine($"  Fully completed:            {fullyCompleted}");
        Console.WriteLine($"  Skipped (no achievements):  {skippedNoAchievements}");
        Console.WriteLine($"  Total HTTP requests:        {requestCount}");
        Console.WriteLine($"  Wall-clock duration:        {elapsed:hh\\:mm\\:ss}");
    }

    private static void PrintTable(string title, IReadOnlyList<(OwnedGame Game, GameEffort Effort, string Why)> rows)
    {
        Console.WriteLine();
        Console.WriteLine($"{title} — top {rows.Count} by remaining effort:");
        Console.WriteLine();

        var header = string.Format(
            "{0,4}  {1,-" + NameColumnWidth + "}  {2,-11}  {3,6}  {4,8}  {5}",
            "#", "Game", "Progress", "Pct", "Effort", "Why");
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length + 20));

        for (var i = 0; i < rows.Count; i++)
        {
            var (game, effort, why) = rows[i];
            var progressText = $"{effort.UnlockedCount}/{effort.TotalCount}";
            Console.WriteLine(string.Format(
                "{0,4}  {1,-" + NameColumnWidth + "}  {2,-11}  {3,5:F1}%  {4,8:F2}  {5}",
                i + 1, Truncate(game.Name, NameColumnWidth), progressText, effort.CompletionPercent, effort.RemainingEffort, why));
        }
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
