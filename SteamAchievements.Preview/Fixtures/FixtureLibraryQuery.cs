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

    // Not in spec section 11's list. The circuit-breaker Notice is one of the
    // five that section 7 requires, and once other-account renders its own
    // notice — which is what its name promises — no scenario reaches the
    // circuit breaker at all.
    CircuitOpen,
}

public sealed class FixtureLibraryQuery : ILibraryQuery
{
    /// <summary>
    /// The library size the mockup shows. The fixtures are fourteen games chosen
    /// to exercise the ranking rules, so the counts on the sidebar and in the
    /// history are stated rather than derived from them.
    /// </summary>
    private const int MockGames = 1482;

    private const int MockAchievements = 61214;

    public Scenario Scenario { get; set; } = Scenario.Normal;

    private IReadOnlyList<FixtureGame> Source => Scenario switch
    {
        Scenario.Empty or Scenario.PrivateProfile or Scenario.InvalidKey => [],

        // Divinity with its rarity stripped entirely. Taking the game as-is
        // would NOT reach this state: EffortCalculator reports RarityUnknown
        // only when every achievement lacks a percentage, and Divinity has one
        // for 33 of its 39 locked achievements. Without this the scenario
        // would promise a state it never shows.
        Scenario.RarityUnknown => FixtureData.All
            .Where(g => g.Game.AppId == 435150)
            .Select(g => g with
            {
                Achievements = g.Achievements
                    .Select(a => a with { GlobalPercent = (double?)null })
                    .ToList(),
            })
            .ToList(),

        _ => FixtureData.All,
    };

    public QueueView GetQueue(DateTimeOffset now)
    {
        var rows = Source.Select(g => QueueRowBuilder.Build(g.Game, g.Achievements)).ToList();

        // The mockup's denominator counts the whole library, achievements or not.
        return new QueueView(rows, rows.Count == 0 ? 0 : MockGames);
    }

    public GameDetailView? GetGame(uint appId, DateTimeOffset now)
    {
        var game = Source.FirstOrDefault(g => g.Game.AppId == appId);
        return game is null ? null : GameDetailBuilder.Build(game.Game, game.Achievements, now);
    }

    public LibrarySummary GetSummary(DateTimeOffset now) =>
        Source.Count == 0
            ? LibrarySummary.Build(0, 0, lastSync: null, now)
            : LibrarySummary.Build(MockGames, MockAchievements, now.AddMinutes(-14), now);

    public IReadOnlyList<SyncRunView> GetSyncHistory(int limit, DateTimeOffset now)
    {
        if (Source.Count == 0)
        {
            return [];
        }

        (DateTimeOffset At, string Kind, long Games, long DurationMs)[] runs =
        [
            (now.AddMinutes(-14), "incremental",         4L,   2149L),
            (now.AddHours(-13),   "incremental",         1L,    910L),
            (now.AddDays(-4),     "full",        MockGames, 531000L),
            (now.AddDays(-4),     "schema",            214L,  66000L),
        ];

        return runs
            .Take(limit)
            .Select(r => new SyncRunView(
                Formatting.Timestamp(r.At, now),
                SyncRunView.Describe(r.Kind, r.Games, error: null),
                Formatting.Duration(r.DurationMs),
                SyncRunOutcome.Completed))
            .ToList();
    }
}
