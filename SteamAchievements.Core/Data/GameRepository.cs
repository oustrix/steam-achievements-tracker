using Dapper;
using Microsoft.Data.Sqlite;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Data;

public sealed class GameRepository
{
    private readonly SqliteConnection _connection;

    // Microsoft.Data.Sqlite connections are not safe to use concurrently from
    // multiple threads, and ADO.NET only permits one active transaction per
    // connection — several methods below (UpsertSchema, UpsertGlobalPercentages,
    // ...) open their own transaction internally. Two callers racing to
    // BeginTransaction on this connection throw "SQLite Error 1: 'cannot start
    // a transaction within a transaction'" (or worse, interleave writes
    // silently). Every public method that touches the connection is therefore
    // serialized through this lock, so any caller — a sync worker pool today,
    // a UI reading progress during a background sync tomorrow — is safe by
    // default without needing to know this connection isn't thread-safe.
    //
    // This is a non-reentrant lock: no method below may call another public
    // method of this class while holding it, or the second acquisition would
    // deadlock. Checked — none currently do; each talks to _connection directly.
    private readonly Lock _lock = new();

    public GameRepository(SqliteConnection connection) => _connection = connection;

    /// <summary>Exposed so tests can assert with raw SQL.</summary>
    public SqliteConnection Connection => _connection;

    public void UpsertOwnedGames(IReadOnlyList<OwnedGame> games)
    {
        lock (_lock)
        {
            using var transaction = _connection.BeginTransaction();

            foreach (var game in games)
            {
                _connection.Execute("""
                    INSERT INTO games (app_id, name, icon_hash) VALUES (@AppId, @Name, @IconHash)
                    ON CONFLICT(app_id) DO UPDATE SET name = excluded.name, icon_hash = excluded.icon_hash;

                    INSERT INTO owned_games (app_id, playtime_forever, playtime_2weeks, last_played_at)
                    VALUES (@AppId, @PlaytimeForever, @PlaytimeTwoWeeks, @LastPlayed)
                    ON CONFLICT(app_id) DO UPDATE SET
                        playtime_forever = excluded.playtime_forever,
                        playtime_2weeks  = excluded.playtime_2weeks,
                        last_played_at   = excluded.last_played_at;

                    INSERT INTO sync_state (app_id) VALUES (@AppId)
                    ON CONFLICT(app_id) DO NOTHING;
                    """,
                    new
                    {
                        game.AppId,
                        game.Name,
                        game.IconHash,
                        game.PlaytimeForever,
                        game.PlaytimeTwoWeeks,
                        LastPlayed = game.LastPlayed?.ToString("o"),
                    }, transaction);
            }

            transaction.Commit();
        }
    }

    // Dapper cannot map into ValueTuple, and it does not translate snake_case
    // to PascalCase by default. Every query therefore aliases its columns
    // explicitly and materializes into a private row record.
    //
    // Row records use `long` for every SQLite INTEGER column (never `int` or
    // `uint`): Dapper's constructor-based materializer for parameterless-ctor-less
    // types (records included) requires an exact CLR type match against the
    // reader's column type, and Microsoft.Data.Sqlite always reports INTEGER
    // columns as Int64. A record declaring `uint`/`int` there throws
    // InvalidOperationException at read time. Narrowing happens explicitly in
    // the projection below.
    private sealed record OwnedGameRow(long AppId, string Name, string IconHash,
        long PlaytimeForever, long PlaytimeTwoWeeks, string? LastPlayedAt);

    public IReadOnlyList<OwnedGame> GetOwnedGames()
    {
        lock (_lock)
        {
            return _connection.Query<OwnedGameRow>("""
                SELECT g.app_id            AS AppId,
                       g.name              AS Name,
                       g.icon_hash         AS IconHash,
                       o.playtime_forever  AS PlaytimeForever,
                       o.playtime_2weeks   AS PlaytimeTwoWeeks,
                       o.last_played_at    AS LastPlayedAt
                FROM owned_games o JOIN games g ON g.app_id = o.app_id
                """)
                .Select(r => new OwnedGame((uint)r.AppId, r.Name, r.IconHash, (int)r.PlaytimeForever, (int)r.PlaytimeTwoWeeks,
                    r.LastPlayedAt is null ? null : DateTimeOffset.Parse(r.LastPlayedAt)))
                .ToList();
        }
    }

    public void UpsertSchema(uint appId, IReadOnlyList<AchievementSchema> schema, DateTimeOffset now)
    {
        lock (_lock)
        {
            using var transaction = _connection.BeginTransaction();

            // achievements.app_id has a foreign key against games(app_id). Schema can
            // legitimately be synced for a game the caller hasn't (yet) upserted via
            // UpsertOwnedGames in this connection's lifetime, so ensure a placeholder
            // row exists; UpsertOwnedGames' real name/icon_hash values win on conflict.
            //
            // sync_state must get the same placeholder treatment: the bookkeeping
            // UPDATE below (schema_synced_at) is a silent no-op against a WHERE
            // clause matching zero rows, which would leave schema_synced_at NULL
            // forever and make Task 7's 30-day TTL check treat the schema as
            // perpetually stale.
            _connection.Execute(
                "INSERT INTO games (app_id, name) VALUES (@AppId, '') ON CONFLICT(app_id) DO NOTHING",
                new { AppId = appId }, transaction);
            _connection.Execute(
                "INSERT INTO sync_state (app_id) VALUES (@AppId) ON CONFLICT(app_id) DO NOTHING",
                new { AppId = appId }, transaction);

            foreach (var achievement in schema)
            {
                // first_seen_at is written once and never overwritten: it is the
                // only signal we will have for future DLC grouping.
                _connection.Execute("""
                    INSERT INTO achievements
                        (app_id, api_name, display_name, description, icon_url, icon_gray_url,
                         is_hidden, sort_order, first_seen_at)
                    VALUES (@AppId, @ApiName, @DisplayName, @Description, @IconUrl, @IconGrayUrl,
                            @IsHidden, @SortOrder, @Now)
                    ON CONFLICT(app_id, api_name) DO UPDATE SET
                        display_name  = excluded.display_name,
                        description   = excluded.description,
                        icon_url      = excluded.icon_url,
                        icon_gray_url = excluded.icon_gray_url,
                        is_hidden     = excluded.is_hidden,
                        sort_order    = excluded.sort_order;
                    """,
                    new
                    {
                        AppId = appId,
                        achievement.ApiName,
                        achievement.DisplayName,
                        achievement.Description,
                        achievement.IconUrl,
                        achievement.IconGrayUrl,
                        IsHidden = achievement.IsHidden ? 1 : 0,
                        achievement.SortOrder,
                        Now = now.ToString("o"),
                    }, transaction);
            }

            _connection.Execute(
                "UPDATE sync_state SET schema_synced_at = @Now WHERE app_id = @AppId",
                new { AppId = appId, Now = now.ToString("o") }, transaction);

            transaction.Commit();
        }
    }

    private sealed record AchievementRow(string ApiName, string DisplayName, string Description,
        string IconUrl, string IconGrayUrl, long IsHidden, long SortOrder, string FirstSeenAt);

    public IReadOnlyList<StoredAchievement> GetAchievements(uint appId)
    {
        lock (_lock)
        {
            return _connection.Query<AchievementRow>("""
                SELECT api_name       AS ApiName,
                       display_name   AS DisplayName,
                       description    AS Description,
                       icon_url       AS IconUrl,
                       icon_gray_url  AS IconGrayUrl,
                       is_hidden      AS IsHidden,
                       sort_order     AS SortOrder,
                       first_seen_at  AS FirstSeenAt
                FROM achievements WHERE app_id = @AppId ORDER BY sort_order
                """, new { AppId = appId })
                .Select(r => new StoredAchievement(r.ApiName, r.DisplayName, r.Description, r.IconUrl,
                    r.IconGrayUrl, r.IsHidden == 1, (int)r.SortOrder, DateTimeOffset.Parse(r.FirstSeenAt)))
                .ToList();
        }
    }

    public void UpsertGlobalPercentages(uint appId, IReadOnlyDictionary<string, double> percentages)
    {
        lock (_lock)
        {
            using var transaction = _connection.BeginTransaction();
            var now = DateTimeOffset.UtcNow.ToString("o");

            // Same gap as UpsertSchema: global percentages can be synced for a game
            // whose sync_state row doesn't exist yet, which would make the
            // bookkeeping UPDATE below a silent no-op and leave global_synced_at
            // NULL forever.
            _connection.Execute(
                "INSERT INTO sync_state (app_id) VALUES (@AppId) ON CONFLICT(app_id) DO NOTHING",
                new { AppId = appId }, transaction);

            foreach (var (apiName, percent) in percentages)
            {
                _connection.Execute("""
                    INSERT INTO global_percents (app_id, api_name, percent, fetched_at)
                    VALUES (@AppId, @ApiName, @Percent, @Now)
                    ON CONFLICT(app_id, api_name) DO UPDATE SET
                        percent = excluded.percent, fetched_at = excluded.fetched_at;
                    """, new { AppId = appId, ApiName = apiName, Percent = percent, Now = now }, transaction);
            }

            _connection.Execute("UPDATE sync_state SET global_synced_at = @Now WHERE app_id = @AppId",
                new { AppId = appId, Now = now }, transaction);
            transaction.Commit();
        }
    }

    public void UpsertPlayerAchievements(uint appId, IReadOnlyList<PlayerAchievement> progress)
    {
        lock (_lock)
        {
            using var transaction = _connection.BeginTransaction();

            foreach (var item in progress)
            {
                _connection.Execute("""
                    INSERT INTO player_achievements (app_id, api_name, unlocked, unlocked_at)
                    VALUES (@AppId, @ApiName, @Unlocked, @UnlockedAt)
                    ON CONFLICT(app_id, api_name) DO UPDATE SET
                        unlocked = excluded.unlocked, unlocked_at = excluded.unlocked_at;
                    """,
                    new
                    {
                        AppId = appId,
                        item.ApiName,
                        Unlocked = item.Unlocked ? 1 : 0,
                        UnlockedAt = item.UnlockedAt?.ToString("o"),
                    }, transaction);
            }

            transaction.Commit();
        }
    }

    private sealed record SyncStateRow(long AppId, long HasAchievements, long SyncedPlaytime,
        string? SchemaSyncedAt, string? GlobalSyncedAt, string? PlayerSyncedAt);

    public IReadOnlyDictionary<uint, GameSyncState> GetSyncStates()
    {
        lock (_lock)
        {
            return _connection.Query<SyncStateRow>("""
                SELECT app_id            AS AppId,
                       has_achievements  AS HasAchievements,
                       synced_playtime   AS SyncedPlaytime,
                       schema_synced_at  AS SchemaSyncedAt,
                       global_synced_at  AS GlobalSyncedAt,
                       player_synced_at  AS PlayerSyncedAt
                FROM sync_state
                """)
                .ToDictionary(r => (uint)r.AppId, r => new GameSyncState(
                    (uint)r.AppId,
                    r.HasAchievements == 1,
                    (int)r.SyncedPlaytime,
                    r.SchemaSyncedAt is null ? null : DateTimeOffset.Parse(r.SchemaSyncedAt),
                    r.GlobalSyncedAt is null ? null : DateTimeOffset.Parse(r.GlobalSyncedAt),
                    r.PlayerSyncedAt is null ? null : DateTimeOffset.Parse(r.PlayerSyncedAt)));
        }
    }

    public void MarkSynced(uint appId, int playtime, DateTimeOffset now)
    {
        lock (_lock)
        {
            _connection.Execute("""
                UPDATE sync_state
                SET synced_playtime = @Playtime, player_synced_at = @Now, last_error = NULL
                WHERE app_id = @AppId
                """, new { AppId = appId, Playtime = playtime, Now = now.ToString("o") });
        }
    }

    public void MarkNoAchievements(uint appId)
    {
        lock (_lock)
        {
            _connection.Execute("UPDATE sync_state SET has_achievements = 0 WHERE app_id = @AppId",
                new { AppId = appId });
        }
    }

    public void MarkError(uint appId, string error)
    {
        lock (_lock)
        {
            _connection.Execute("UPDATE sync_state SET last_error = @Error WHERE app_id = @AppId",
                new { AppId = appId, Error = error });
        }
    }

    private sealed record ProgressRow(string ApiName, string DisplayName, string Description,
        string IconUrl, long IsHidden, long? Unlocked, string? UnlockedAt, double? Percent);

    public IReadOnlyList<AchievementProgress> GetGameProgress(uint appId)
    {
        lock (_lock)
        {
            return _connection.Query<ProgressRow>("""
                SELECT a.api_name      AS ApiName,
                       a.display_name  AS DisplayName,
                       a.description   AS Description,
                       a.icon_url      AS IconUrl,
                       a.is_hidden     AS IsHidden,
                       p.unlocked      AS Unlocked,
                       p.unlocked_at   AS UnlockedAt,
                       gp.percent      AS Percent
                FROM achievements a
                LEFT JOIN player_achievements p ON p.app_id = a.app_id AND p.api_name = a.api_name
                LEFT JOIN global_percents     gp ON gp.app_id = a.app_id AND gp.api_name = a.api_name
                WHERE a.app_id = @AppId
                ORDER BY a.sort_order
                """, new { AppId = appId })
                .Select(r => new AchievementProgress(r.ApiName, r.DisplayName, r.Description, r.IconUrl,
                    r.IsHidden == 1, r.Unlocked == 1,
                    r.UnlockedAt is null ? null : DateTimeOffset.Parse(r.UnlockedAt), r.Percent))
                .ToList();
        }
    }

    /// <summary>
    /// One row per sync. Trend charts are out of scope for the MVP, but this
    /// history cannot be backfilled later, so it is recorded from day one.
    /// </summary>
    public void WriteSnapshot(DateTimeOffset takenAt)
    {
        lock (_lock)
        {
            _connection.Execute("""
                INSERT INTO snapshots (taken_at, unlocked_total, avg_rarity, completion_pct)
                SELECT @TakenAt,
                       COALESCE(SUM(p.unlocked), 0),
                       COALESCE(AVG(CASE WHEN p.unlocked = 1 THEN gp.percent END), 0),
                       COALESCE(100.0 * SUM(p.unlocked) / NULLIF(COUNT(*), 0), 0)
                FROM achievements a
                LEFT JOIN player_achievements p ON p.app_id = a.app_id AND p.api_name = a.api_name
                LEFT JOIN global_percents     gp ON gp.app_id = a.app_id AND gp.api_name = a.api_name
                ON CONFLICT(taken_at) DO NOTHING;
                """, new { TakenAt = takenAt.ToString("o") });
        }
    }
}
