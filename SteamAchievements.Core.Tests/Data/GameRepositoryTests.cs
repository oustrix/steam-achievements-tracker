using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Data;

public class GameRepositoryTests
{
    private static GameRepository InMemory() => new(Database.Open(":memory:"));

    [Fact]
    public void MigrationCreatesAllTables()
    {
        using var connection = Database.Open(":memory:");

        var tables = Dapper.SqlMapper.Query<string>(connection,
            "SELECT name FROM sqlite_master WHERE type = 'table'").ToHashSet();

        Assert.Contains("games", tables);
        Assert.Contains("owned_games", tables);
        Assert.Contains("achievements", tables);
        Assert.Contains("global_percents", tables);
        Assert.Contains("player_achievements", tables);
        Assert.Contains("sync_state", tables);
        Assert.Contains("snapshots", tables);
        Assert.Contains("settings", tables);
    }

    [Fact]
    public void UpsertOwnedGamesIsIdempotent()
    {
        var repository = InMemory();
        var game = new OwnedGame(292030, "The Witcher 3", "abc", 100, 10, null);

        repository.UpsertOwnedGames([game]);
        repository.UpsertOwnedGames([game with { PlaytimeForever = 200 }]);

        var stored = repository.GetOwnedGames();
        Assert.Single(stored);
        Assert.Equal(200, stored[0].PlaytimeForever);
    }

    [Fact]
    public void UpsertSchemaPreservesFirstSeenAcrossReSyncs()
    {
        var repository = InMemory();
        var schema = new AchievementSchema("ACH_1", "First", "desc", "i", "g", false, 0);
        var firstSeen = DateTimeOffset.UnixEpoch.AddDays(1);

        repository.UpsertSchema(292030, [schema], firstSeen);
        repository.UpsertSchema(292030, [schema with { DisplayName = "Renamed" }], firstSeen.AddDays(30));

        var stored = repository.GetAchievements(292030).Single();
        Assert.Equal("Renamed", stored.DisplayName);
        Assert.Equal(firstSeen, stored.FirstSeenAt);
    }

    [Fact]
    public void MarkNoAchievementsStopsGameFromBeingSyncedAgain()
    {
        var repository = InMemory();
        repository.UpsertOwnedGames([new OwnedGame(220, "Half-Life 2", "x", 0, 0, null)]);

        repository.MarkNoAchievements(220);

        Assert.False(repository.GetSyncStates()[220].HasAchievements);
    }

    [Fact]
    public void GetGameProgressJoinsSchemaProgressAndRarity()
    {
        var repository = InMemory();
        repository.UpsertOwnedGames([new OwnedGame(292030, "The Witcher 3", "abc", 100, 0, null)]);
        repository.UpsertSchema(292030,
        [
            new AchievementSchema("ACH_1", "First", "", "i", "g", false, 0),
            new AchievementSchema("ACH_2", "Second", "", "i", "g", true, 1),
        ], DateTimeOffset.UnixEpoch);
        repository.UpsertGlobalPercentages(292030, new Dictionary<string, double> { ["ACH_1"] = 62.4, ["ACH_2"] = 0.4 });
        repository.UpsertPlayerAchievements(292030,
        [
            new PlayerAchievement("ACH_1", true, DateTimeOffset.UnixEpoch),
            new PlayerAchievement("ACH_2", false, null),
        ]);

        var progress = repository.GetGameProgress(292030);

        Assert.Equal(2, progress.Count);
        Assert.True(progress.Single(p => p.ApiName == "ACH_1").Unlocked);
        Assert.Equal(0.4, progress.Single(p => p.ApiName == "ACH_2").GlobalPercent);
    }

    [Fact]
    public void WriteSnapshotRecordsCurrentTotals()
    {
        var repository = InMemory();
        repository.UpsertOwnedGames([new OwnedGame(292030, "The Witcher 3", "abc", 100, 0, null)]);
        repository.UpsertSchema(292030,
        [
            new AchievementSchema("ACH_1", "First", "", "i", "g", false, 0),
            new AchievementSchema("ACH_2", "Second", "", "i", "g", false, 1),
        ], DateTimeOffset.UnixEpoch);
        repository.UpsertPlayerAchievements(292030,
        [
            new PlayerAchievement("ACH_1", true, DateTimeOffset.UnixEpoch),
            new PlayerAchievement("ACH_2", false, null),
        ]);

        repository.WriteSnapshot(new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero));

        var unlocked = Dapper.SqlMapper.QuerySingle<long>(
            repository.Connection, "SELECT unlocked_total FROM snapshots");
        var completion = Dapper.SqlMapper.QuerySingle<double>(
            repository.Connection, "SELECT completion_pct FROM snapshots");

        Assert.Equal(1, unlocked);
        Assert.Equal(50, completion, 6);
    }
}
