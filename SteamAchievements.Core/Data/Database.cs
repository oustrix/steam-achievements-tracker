using Dapper;
using Microsoft.Data.Sqlite;

namespace SteamAchievements.Core.Data;

public static class Database
{
    /// <summary>
    /// Shared by every writable connection so they cannot drift apart. See
    /// <see cref="Open"/> for why each pragma is set the way it is.
    /// </summary>
    private const string WritablePragmas =
        "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; "
        + "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";

    /// <summary>
    /// A writable connection: the sync engine's, and the accent picker's.
    ///
    /// WAL lets readers run alongside a writer but still permits only one
    /// writer at a time, so a write issued while a sync holds the write lock
    /// would fail immediately with SQLITE_BUSY. <c>busy_timeout</c> turns that
    /// into a wait — a single-row update against <c>settings</c> finishes in
    /// microseconds, and waiting out a sync's transaction is invisible.
    /// </summary>
    public static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        // synchronous = NORMAL skips an fsync on every commit under WAL
        // (only the WAL checkpoint fsyncs). This database is a rebuildable
        // local cache of Steam's own data — anything lost in an OS crash or
        // power loss is simply re-fetched by the next sync — so trading the
        // stronger FULL guarantee for faster commits is the right call here.
        connection.Execute(WritablePragmas);
        Migrate(connection);
        return connection;
    }

    /// <summary>
    /// A second connection for readers — the UI — alongside the sync engine's
    /// own. <see cref="GameRepository"/> is not thread-safe and
    /// <c>SyncOrchestrator</c> already serializes every call to it behind a
    /// lock; sharing that connection with the UI would put reads back inside
    /// the same contention. WAL lets a reader run concurrently with the
    /// writer, so the UI simply gets its own handle.
    ///
    /// Deliberately not <c>Mode=ReadOnly</c>: a read-only SQLite connection to
    /// a WAL database still needs write access to the shared-memory index
    /// file, so that mode fails in exactly the configuration this is for. The
    /// guarantee here is by construction — callers issue only SELECTs — and
    /// the name says so.
    ///
    /// Skips <see cref="Migrate"/>: schema ownership belongs to the writer.
    /// </summary>
    public static SqliteConnection OpenRead(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        connection.Execute("PRAGMA busy_timeout = 5000;");
        return connection;
    }

    /// <summary>
    /// A third connection, for the settings and journal writers. Identical to
    /// <see cref="Open"/> except that it skips <see cref="Migrate"/>.
    ///
    /// Schema ownership belongs to the writer, which has already migrated this
    /// file by the time the host opens this one. Running it again would replay
    /// nine <c>CREATE TABLE IF NOT EXISTS</c> statements and two
    /// <c>pragma_table_info</c> scans before the window is shown, to no effect.
    /// </summary>
    public static SqliteConnection OpenSettings(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        connection.Execute(WritablePragmas);
        return connection;
    }

    /// <summary>
    /// Exposed so a migration can be applied to a connection that was opened
    /// by other means, and so tests can assert it is idempotent.
    /// </summary>
    public static void Migrate(SqliteConnection connection)
    {
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS settings (
                id                 INTEGER PRIMARY KEY CHECK (id = 1),
                steam_id64         TEXT,
                persona_name       TEXT,
                avatar_url         TEXT,
                last_full_sync_at  TEXT
            );

            CREATE TABLE IF NOT EXISTS games (
                app_id            INTEGER PRIMARY KEY,
                name              TEXT NOT NULL,
                icon_hash         TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS owned_games (
                app_id            INTEGER PRIMARY KEY REFERENCES games(app_id) ON DELETE CASCADE,
                playtime_forever  INTEGER NOT NULL DEFAULT 0,
                playtime_2weeks   INTEGER NOT NULL DEFAULT 0,
                last_played_at    TEXT
            );

            CREATE TABLE IF NOT EXISTS achievements (
                app_id         INTEGER NOT NULL REFERENCES games(app_id) ON DELETE CASCADE,
                api_name       TEXT NOT NULL,
                display_name   TEXT NOT NULL DEFAULT '',
                description    TEXT NOT NULL DEFAULT '',
                icon_url       TEXT NOT NULL DEFAULT '',
                icon_gray_url  TEXT NOT NULL DEFAULT '',
                is_hidden      INTEGER NOT NULL DEFAULT 0,
                sort_order     INTEGER NOT NULL DEFAULT 0,
                first_seen_at  TEXT NOT NULL,
                PRIMARY KEY (app_id, api_name)
            );

            CREATE TABLE IF NOT EXISTS global_percents (
                app_id      INTEGER NOT NULL,
                api_name    TEXT NOT NULL,
                percent     REAL NOT NULL,
                fetched_at  TEXT NOT NULL,
                PRIMARY KEY (app_id, api_name)
            );

            CREATE TABLE IF NOT EXISTS player_achievements (
                app_id       INTEGER NOT NULL,
                api_name     TEXT NOT NULL,
                unlocked     INTEGER NOT NULL DEFAULT 0,
                unlocked_at  TEXT,
                PRIMARY KEY (app_id, api_name)
            );

            CREATE TABLE IF NOT EXISTS sync_state (
                app_id            INTEGER PRIMARY KEY,
                has_achievements  INTEGER NOT NULL DEFAULT 1,
                synced_playtime   INTEGER NOT NULL DEFAULT -1,
                schema_synced_at  TEXT,
                global_synced_at  TEXT,
                player_synced_at  TEXT,
                last_error        TEXT
            );

            CREATE TABLE IF NOT EXISTS snapshots (
                taken_at        TEXT PRIMARY KEY,
                unlocked_total  INTEGER NOT NULL,
                avg_rarity      REAL NOT NULL,
                completion_pct  REAL NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sync_runs (
                started_at    TEXT PRIMARY KEY,
                kind          TEXT    NOT NULL,
                games_synced  INTEGER NOT NULL,
                duration_ms   INTEGER NOT NULL,
                error         TEXT
            );

            CREATE INDEX IF NOT EXISTS ix_player_achievements_app
                ON player_achievements (app_id);
            """);

        // CREATE TABLE IF NOT EXISTS cannot add a column to a table that
        // already exists, so anything added to an existing table after the
        // first release needs this path.
        EnsureColumn(connection, "settings", "accent", "TEXT");

        // Set when Steam rejects the key, cleared when a key is accepted or a
        // sync succeeds. Persisted rather than held in memory: otherwise a
        // restart makes the application look healthy and the user spends
        // requests rediscovering what was already known.
        EnsureColumn(connection, "settings", "key_rejected_at", "TEXT");
    }

    /// <summary>
    /// Empties the library so a different Steam account can be synced into it,
    /// or so a broken cache can be rebuilt. These are the same operation: no
    /// table carries a SteamID column, so the database implicitly belongs to one
    /// account and mixing two produces silently wrong data with nothing in the
    /// schema to tell them apart afterwards.
    ///
    /// Deletes rows rather than the file. Three connections are open against it
    /// and Windows does not delete open files; tearing the whole connection graph
    /// down and rebuilding it would be far more code than this.
    ///
    /// Keeps <c>settings.accent</c>. That is the user's taste rather than the
    /// account's data, and losing it on "switch account" is a surprise beyond
    /// what the confirmation promised.
    /// </summary>
    public static void ResetLibrary(SqliteConnection connection)
    {
        using (var transaction = connection.BeginTransaction())
        {
            connection.Execute("""
                DELETE FROM player_achievements;
                DELETE FROM global_percents;
                DELETE FROM achievements;
                DELETE FROM sync_state;
                DELETE FROM owned_games;
                DELETE FROM games;
                DELETE FROM snapshots;
                DELETE FROM sync_runs;

                UPDATE settings
                   SET steam_id64        = NULL,
                       persona_name      = NULL,
                       avatar_url        = NULL,
                       last_full_sync_at = NULL,
                       key_rejected_at   = NULL
                 WHERE id = 1;
                """, transaction: transaction);

            transaction.Commit();
        }

        // VACUUM cannot run inside a transaction, so it is deliberately outside
        // the block above. Without it the file keeps the space the deleted rows
        // occupied, which for a 1500-game library is most of it.
        connection.Execute("VACUUM");
    }

    /// <summary>
    /// Idempotent ALTER TABLE. SQLite has no "ADD COLUMN IF NOT EXISTS", and
    /// running the same ALTER twice throws "duplicate column name", so the
    /// current shape is inspected first.
    /// </summary>
    private static void EnsureColumn(SqliteConnection connection, string table, string column, string type)
    {
        var existing = connection.Query<string>(
            $"SELECT name FROM pragma_table_info('{table}')").ToHashSet();

        if (!existing.Contains(column))
        {
            connection.Execute($"ALTER TABLE {table} ADD COLUMN {column} {type}");
        }
    }
}
