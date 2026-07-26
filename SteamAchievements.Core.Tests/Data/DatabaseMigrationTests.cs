using Dapper;
using Microsoft.Data.Sqlite;
using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Tests.Data;

public class DatabaseMigrationTests
{
    [Fact]
    public void CreatesTheSyncRunsTable()
    {
        using var connection = Database.Open(":memory:");

        var tables = connection.Query<string>(
            "SELECT name FROM sqlite_master WHERE type = 'table'").ToHashSet();

        Assert.Contains("sync_runs", tables);
    }

    [Fact]
    public void AddsTheAccentColumnToASettingsTableThatPredatesIt()
    {
        // Simulates an existing installation: settings exists in its original
        // shape, and CREATE TABLE IF NOT EXISTS will leave it untouched.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        connection.Execute("""
            CREATE TABLE settings (
                id                 INTEGER PRIMARY KEY CHECK (id = 1),
                steam_id64         TEXT,
                persona_name       TEXT,
                avatar_url         TEXT,
                last_full_sync_at  TEXT
            );
            """);

        Database.Migrate(connection);

        var columns = connection.Query<string>("SELECT name FROM pragma_table_info('settings')")
            .ToHashSet();
        Assert.Contains("accent", columns);
    }

    [Fact]
    public void RunningTheMigrationTwiceIsANoOp()
    {
        using var connection = Database.Open(":memory:");

        Database.Migrate(connection);   // must not throw "duplicate column name"

        var columns = connection.Query<string>("SELECT name FROM pragma_table_info('settings')")
            .Count(name => name == "accent");
        Assert.Equal(1, columns);
    }

    [Fact]
    public void OpenReadReturnsAUsableConnectionWithoutMigrating()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sat-{Guid.NewGuid():N}.db");

        try
        {
            using (var writer = Database.Open(path))
            {
                writer.Execute("INSERT INTO games (app_id, name) VALUES (620, 'Portal 2')");
            }

            using var reader = Database.OpenRead(path);

            Assert.Equal("Portal 2",
                reader.QuerySingle<string>("SELECT name FROM games WHERE app_id = 620"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The mitigation belongs to the writer. WAL permits only one writer at a
    /// time, so writing the accent while a sync holds the write lock fails
    /// immediately with SQLITE_BUSY unless the connection is willing to wait.
    /// Nothing else in the process sets this, so a connection that arrives
    /// without it makes the accent picker fail under exactly the conditions it
    /// was designed to survive.
    /// </summary>
    [Fact]
    public void WritableConnectionsWaitOutAnotherWriterInsteadOfFailing()
    {
        using var connection = Database.Open(":memory:");

        Assert.Equal(5000, connection.QuerySingle<long>("PRAGMA busy_timeout"));
    }

    [Fact]
    public void ReadOnlyConnectionsCarryTheSameTimeout()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sat-{Guid.NewGuid():N}.db");

        try
        {
            using (Database.Open(path)) { }

            // Open and OpenRead build the same connection string, so disposing
            // the writer returns its handle — busy timeout already set — to the
            // pool that OpenRead draws from. Without this the assertion below
            // passes whether or not OpenRead sets the pragma at all, which is
            // coverage that certifies nothing.
            SqliteConnection.ClearAllPools();

            using var reader = Database.OpenRead(path);

            Assert.Equal(5000, reader.QuerySingle<long>("PRAGMA busy_timeout"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadingAndWritingThroughSeparateConnectionsDoesNotConflict()
    {
        // The UI reads through its own connection while the sync engine writes
        // through another. WAL permits exactly this; without it the reader
        // would block or fail.
        var path = Path.Combine(Path.GetTempPath(), $"sat-{Guid.NewGuid():N}.db");

        try
        {
            using var writer = Database.Open(path);
            using var reader = Database.OpenRead(path);

            writer.Execute("INSERT INTO games (app_id, name) VALUES (620, 'Portal 2')");

            Assert.Equal(1, reader.QuerySingle<long>("SELECT COUNT(*) FROM games"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
