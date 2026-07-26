using Dapper;
using Microsoft.Data.Sqlite;

namespace SteamAchievements.Core.Data;

public static class Database
{
    public static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        connection.Execute("PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;");
        Migrate(connection);
        return connection;
    }

    private static void Migrate(SqliteConnection connection)
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

            CREATE INDEX IF NOT EXISTS ix_player_achievements_app
                ON player_achievements (app_id);
            """);
    }
}
