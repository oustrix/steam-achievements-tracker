using Dapper;
using Microsoft.Data.Sqlite;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Data;

/// <summary>
/// Reads the whole library in two queries and assembles the views in memory.
///
/// It deliberately does not go through <see cref="GameRepository"/>: that type
/// wraps the sync engine's connection, is not thread-safe, and every call to
/// it is already serialized behind a lock. This one owns a separate reader
/// connection from <see cref="Database.OpenRead"/> instead.
/// </summary>
public sealed class SqliteLibraryQuery : ILibraryQuery
{
    private readonly SqliteConnection _connection;

    public SqliteLibraryQuery(SqliteConnection connection) => _connection = connection;

    // Row records use long for every INTEGER column and narrow in the
    // projection: Microsoft.Data.Sqlite reports INTEGER as Int64 and Dapper's
    // record materializer needs an exact CLR type match.
    private sealed record GameRow(long AppId, string Name, long PlaytimeForever,
        long PlaytimeTwoWeeks, string? LastPlayedAt);

    private sealed record ProgressRow(long AppId, string ApiName, string DisplayName,
        string Description, string IconUrl, long IsHidden, long? Unlocked,
        string? UnlockedAt, double? Percent);

    private sealed record SyncRunRow(string StartedAt, string Kind, long GamesSynced,
        long DurationMs, string? Error);

    private const string GamesSql = """
        SELECT g.app_id            AS AppId,
               g.name              AS Name,
               o.playtime_forever  AS PlaytimeForever,
               o.playtime_2weeks   AS PlaytimeTwoWeeks,
               o.last_played_at    AS LastPlayedAt
        FROM owned_games o JOIN games g ON g.app_id = o.app_id
        """;

    /// <summary>
    /// One projection for both callers. The whole-library and single-game
    /// reads differ only by a WHERE clause, and writing them out twice is how
    /// a new column ends up added to one and forgotten in the other.
    /// </summary>
    private static string ProgressSql(bool singleGame) => $"""
        SELECT a.app_id        AS AppId,
               a.api_name      AS ApiName,
               a.display_name  AS DisplayName,
               a.description   AS Description,
               a.icon_url      AS IconUrl,
               a.is_hidden     AS IsHidden,
               p.unlocked      AS Unlocked,
               p.unlocked_at   AS UnlockedAt,
               gp.percent      AS Percent
        FROM achievements a
        LEFT JOIN player_achievements p  ON p.app_id  = a.app_id AND p.api_name  = a.api_name
        LEFT JOIN global_percents     gp ON gp.app_id = a.app_id AND gp.api_name = a.api_name
        {(singleGame ? "WHERE a.app_id = @AppId" : "")}
        ORDER BY a.app_id, a.sort_order
        """;

    public QueueView GetQueue(DateTimeOffset now)
    {
        var games = _connection.Query<GameRow>(GamesSql).ToList();
        var progress = _connection.Query<ProgressRow>(ProgressSql(singleGame: false))
            .GroupBy(r => r.AppId)
            .ToDictionary(g => g.Key, g => g.Select(Project).ToList());

        var rows = games
            .Where(g => progress.ContainsKey(g.AppId))
            .Select(g => QueueRowBuilder.Build(Game(g), progress[g.AppId]))
            .ToList();

        // The denominator the mockup shows is the whole library, including the
        // 30-40% of it that has no achievements at all.
        return new QueueView(rows, games.Count);
    }

    public GameDetailView? GetGame(uint appId, DateTimeOffset now)
    {
        var game = _connection.QuerySingleOrDefault<GameRow>(
            $"{GamesSql} WHERE g.app_id = @AppId", new { AppId = appId });

        if (game is null)
        {
            return null;
        }

        var achievements = _connection
            .Query<ProgressRow>(ProgressSql(singleGame: true), new { AppId = appId })
            .Select(Project)
            .ToList();

        return GameDetailBuilder.Build(Game(game), achievements, now);
    }

    public LibrarySummary GetSummary(DateTimeOffset now)
    {
        var games = (int)_connection.QuerySingle<long>("SELECT COUNT(*) FROM owned_games");
        var achievements = (int)_connection.QuerySingle<long>("SELECT COUNT(*) FROM achievements");
        var lastSync = _connection.QuerySingleOrDefault<string?>(
            "SELECT last_full_sync_at FROM settings WHERE id = 1");

        return new LibrarySummary(
            games,
            achievements,
            $"{Formatting.Number(games)} games · {Formatting.Number(achievements)} ach.",
            lastSync is null
                ? "Never synced"
                : $"Last sync {Formatting.Relative(DateTimeOffset.Parse(lastSync), now)}");
    }

    public IReadOnlyList<SyncRunView> GetSyncHistory(int limit, DateTimeOffset now) =>
        _connection.Query<SyncRunRow>("""
            SELECT started_at    AS StartedAt,
                   kind          AS Kind,
                   games_synced  AS GamesSynced,
                   duration_ms   AS DurationMs,
                   error         AS Error
            FROM sync_runs ORDER BY started_at DESC LIMIT @Limit
            """, new { Limit = limit })
            .Select(r => Project(r, now))
            .ToList();

    private static SyncRunView Project(SyncRunRow run, DateTimeOffset now)
    {
        // Decoded once and passed on. The sentinel that distinguishes a
        // cancelled run from a failed one lives in the error column, so it must
        // appear in exactly one place — two switches over the same magic string
        // are two things to keep in agreement.
        var outcome = run.Error switch
        {
            null => SyncRunOutcome.Completed,
            SyncJournal.Cancelled => SyncRunOutcome.Cancelled,
            _ => SyncRunOutcome.Failed,
        };

        return new SyncRunView(
            Formatting.Timestamp(DateTimeOffset.Parse(run.StartedAt), now),
            Describe(run, outcome),
            Formatting.Duration(run.DurationMs),
            outcome);
    }

    private static string Describe(SyncRunRow run, SyncRunOutcome outcome)
    {
        var count = Formatting.Number(run.GamesSynced);

        if (outcome == SyncRunOutcome.Cancelled)
        {
            return $"Cancelled — {count} games";
        }

        if (outcome == SyncRunOutcome.Failed)
        {
            return $"Failed — {run.Error}";
        }

        return run.Kind switch
        {
            "full" => $"Full sync — {count} games",
            "incremental" => $"Incremental — {count} games changed",
            "schema" => $"Schema refresh — {count} games stale",
            _ => $"{run.Kind} — {count} games",
        };
    }

    private static OwnedGame Game(GameRow row) => new(
        (uint)row.AppId, row.Name, string.Empty,
        (int)row.PlaytimeForever, (int)row.PlaytimeTwoWeeks,
        row.LastPlayedAt is null ? null : DateTimeOffset.Parse(row.LastPlayedAt));

    private static AchievementProgress Project(ProgressRow row) => new(
        row.ApiName, row.DisplayName, row.Description, row.IconUrl,
        row.IsHidden == 1, row.Unlocked == 1,
        row.UnlockedAt is null ? null : DateTimeOffset.Parse(row.UnlockedAt),
        row.Percent);
}
