using Dapper;
using Microsoft.Data.Sqlite;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Data;

public class DatabaseResetTests
{
    private static readonly ulong SteamId = 76561190000000002;

    private static SqliteConnection Populated()
    {
        var connection = Database.Open(":memory:");
        var repository = new GameRepository(connection);

        repository.UpsertOwnedGames([new OwnedGame(220, "Half-Life 2", "hash", 600, 0, null)]);
        repository.UpsertSchema(220, [new AchievementSchema("A", "First", "", "", "", false, 0)], DateTimeOffset.UtcNow);
        repository.UpsertPlayerAchievements(220, [new PlayerAchievement("A", true, DateTimeOffset.UtcNow)]);
        repository.WriteSnapshot(DateTimeOffset.UtcNow);

        new SqliteAccountStore(connection).Set(SteamId, "oustrix", "avatar");
        new SqliteUserPreferences(connection).SetAccent("#c98f7a");
        connection.Execute("""
            INSERT INTO sync_runs (started_at, kind, games_synced, duration_ms, error)
            VALUES ('2026-07-26T10:00:00.0000000+00:00', 'full', 1, 1000, NULL)
            """);

        return connection;
    }

    [Fact]
    public void RemovesEveryTraceOfTheLibrary()
    {
        using var connection = Populated();

        Database.ResetLibrary(connection);

        foreach (var table in new[]
                 {
                     "games", "owned_games", "achievements", "global_percents",
                     "player_achievements", "sync_state", "snapshots", "sync_runs",
                 })
        {
            Assert.Equal(0, connection.QuerySingle<long>($"SELECT COUNT(*) FROM {table}"));
        }
    }

    [Fact]
    public void ForgetsTheAccount()
    {
        using var connection = Populated();

        Database.ResetLibrary(connection);

        Assert.Null(new SqliteAccountStore(connection).Current);
    }

    [Fact]
    public void KeepsTheAccentBecauseItIsTasteRatherThanAccountData()
    {
        using var connection = Populated();

        Database.ResetLibrary(connection);

        Assert.Equal("#c98f7a", new SqliteUserPreferences(connection).Accent);
    }

    [Fact]
    public void ClearsTheKeyRejectionFlag()
    {
        using var connection = Populated();
        new SqliteAccountStore(connection).MarkKeyRejected(DateTimeOffset.UtcNow);

        Database.ResetLibrary(connection);

        Assert.Null(new SqliteAccountStore(connection).KeyRejectedAt);
    }

    [Fact]
    public void LeavesTheSchemaIntactSoTheApplicationKeepsWorking()
    {
        using var connection = Populated();

        Database.ResetLibrary(connection);
        new GameRepository(connection).UpsertOwnedGames(
            [new OwnedGame(440, "Team Fortress 2", "hash", 10, 0, null)]);

        Assert.Equal(1, connection.QuerySingle<long>("SELECT COUNT(*) FROM owned_games"));
    }

    [Fact]
    public void IsSafeToRunTwice()
    {
        using var connection = Populated();

        Database.ResetLibrary(connection);
        Database.ResetLibrary(connection);

        Assert.Equal(0, connection.QuerySingle<long>("SELECT COUNT(*) FROM games"));
    }
}
