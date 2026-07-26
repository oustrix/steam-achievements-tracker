using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>
/// Scenarios exist so the empty and error states can be seen without
/// genuinely breaking an API key. They shape the data the screens receive;
/// they are not a gallery of state cards.
/// </summary>
public enum Scenario
{
    Normal,
    Empty,
    InvalidKey,
    PrivateProfile,
    RarityUnknown,
    OtherAccount,
}

public sealed class FixtureLibraryQuery : ILibraryQuery
{
    public Scenario Scenario { get; set; } = Scenario.Normal;

    private IReadOnlyList<FixtureGame> Source => Scenario switch
    {
        Scenario.Empty or Scenario.PrivateProfile or Scenario.InvalidKey => [],
        Scenario.RarityUnknown => FixtureData.All.Where(g => g.Game.AppId == 435150).ToList(),
        _ => FixtureData.All,
    };

    public QueueView GetQueue(DateTimeOffset now)
    {
        var rows = Source.Select(g => QueueRowBuilder.Build(g.Game, g.Achievements)).ToList();

        // The mockup's denominator counts the whole library, achievements or not.
        return new QueueView(rows, rows.Count == 0 ? 0 : 1482);
    }

    public GameDetailView? GetGame(uint appId, DateTimeOffset now)
    {
        var game = Source.FirstOrDefault(g => g.Game.AppId == appId);
        return game is null ? null : GameDetailBuilder.Build(game.Game, game.Achievements, now);
    }

    public LibrarySummary GetSummary(DateTimeOffset now)
    {
        if (Source.Count == 0)
        {
            return new LibrarySummary(0, 0, "0 games · 0 ach.", "Never synced");
        }

        var achievements = Source.Sum(g => g.Achievements.Count);

        return new LibrarySummary(1482, 61214,
            $"{Formatting.Number(1482)} games · {Formatting.Number(61214)} ach.",
            $"Last sync {Formatting.Relative(now.AddMinutes(-14), now)}");
    }

    public IReadOnlyList<SyncRunView> GetSyncHistory(int limit, DateTimeOffset now)
    {
        if (Source.Count == 0)
        {
            return [];
        }

        return new[]
        {
            (now.AddMinutes(-14),  "incremental", 4L,    2149L),
            (now.AddHours(-13),    "incremental", 1L,     910L),
            (now.AddDays(-4),      "full",     1482L,  531000L),
            (now.AddDays(-4),      "schema",    214L,   66000L),
        }
        .Take(limit)
        .Select(r => new SyncRunView(
            Formatting.Timestamp(r.Item1, now),
            r.Item2 switch
            {
                "full" => $"Full sync — {Formatting.Number(r.Item3)} games",
                "incremental" => $"Incremental — {Formatting.Number(r.Item3)} games changed",
                _ => $"Schema refresh — {Formatting.Number(r.Item3)} games stale",
            },
            Formatting.Duration(r.Item4),
            Failed: false))
        .ToList();
    }
}
