using Dapper;
using Microsoft.Data.Sqlite;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Data;

public class SqliteLibraryQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Seeds a database through the write path, exactly as a sync would, so
    /// the query is exercised against real stored shapes rather than hand-made
    /// rows. Returns the same connection: ":memory:" is per-connection, so a
    /// second handle would see an empty database.
    /// </summary>
    private static SqliteConnection Seed()
    {
        var connection = Database.Open(":memory:");
        var repository = new GameRepository(connection);

        repository.UpsertOwnedGames(
        [
            new OwnedGame(367520, "Hollow Knight", "hk", 5040, 0, Now.AddDays(-3)),
            new OwnedGame(620, "Portal 2", "p2", 1860, 0, Now.AddDays(-240)),
            new OwnedGame(431960, "Wallpaper Engine", "we", 60, 0, null),
        ]);

        repository.UpsertSchema(367520,
        [
            new AchievementSchema("HK_1", "Attuned", "Beat the Trial of the Warrior", "i1", "g1", false, 0),
            new AchievementSchema("HK_2", "Steel Soul", "Complete the game in Steel Soul mode", "i2", "g2", false, 1),
        ], Now.AddDays(-1));

        repository.UpsertGlobalPercentages(367520,
            new Dictionary<string, double> { ["HK_1"] = 28.0, ["HK_2"] = 9.8 });

        repository.UpsertPlayerAchievements(367520,
        [
            new PlayerAchievement("HK_1", Unlocked: true, Now.AddDays(-14)),
            new PlayerAchievement("HK_2", Unlocked: false, null),
        ]);

        repository.UpsertSchema(620,
            [new AchievementSchema("P2_1", "Bridge Over Troubling Water", "Solve it", "i", "g", false, 0)],
            Now.AddDays(-1));
        repository.UpsertGlobalPercentages(620, new Dictionary<string, double> { ["P2_1"] = 41.0 });
        repository.UpsertPlayerAchievements(620,
            [new PlayerAchievement("P2_1", Unlocked: true, Now.AddDays(-200))]);

        return connection;
    }

    [Fact]
    public void ReturnsOneRowPerGameThatHasAchievements()
    {
        using var connection = Seed();

        var queue = new SqliteLibraryQuery(connection).GetQueue(Now);

        // Wallpaper Engine has no achievements and must not appear as a row,
        // but it is still part of the library and counts in the total.
        Assert.Equal(["Hollow Knight", "Portal 2"], queue.Rows.Select(r => r.Name).Order());
        Assert.Equal(3, queue.TotalGames);
    }

    [Fact]
    public void CarriesTheGeneratedExplanationThroughToTheRow()
    {
        using var connection = Seed();

        var row = new SqliteLibraryQuery(connection).GetQueue(Now).Rows
            .Single(r => r.AppId == 367520);

        Assert.Equal(1, row.Unlocked);
        Assert.Equal(2, row.Total);
        Assert.Equal("1 left, all above 9% of owners", row.Reason);
        Assert.Equal(84, row.PlaytimeHours);
    }

    [Fact]
    public void MarksAFullyUnlockedGameComplete()
    {
        using var connection = Seed();

        var row = new SqliteLibraryQuery(connection).GetQueue(Now).Rows.Single(r => r.AppId == 620);

        Assert.True(row.Complete);
    }

    [Fact]
    public void ReturnsTheGameDetailForAKnownAppId()
    {
        using var connection = Seed();

        var game = new SqliteLibraryQuery(connection).GetGame(367520, Now);

        Assert.NotNull(game);
        Assert.Equal("Hollow Knight", game.Name);
        Assert.Equal("Steel Soul", game.RemainingAchievements.Single().Name);
        Assert.Equal("Attuned", game.UnlockedAchievements.Single().Name);
    }

    [Fact]
    public void ReturnsNullForAnAppIdThatIsNotInTheLibrary()
    {
        using var connection = Seed();

        Assert.Null(new SqliteLibraryQuery(connection).GetGame(1, Now));
    }

    [Fact]
    public void HandlesAGameWithNoRarityDataAtAll()
    {
        using var connection = Seed();
        var repository = new GameRepository(connection);
        repository.UpsertOwnedGames(
            [new OwnedGame(435150, "Divinity: Original Sin 2", "dos2", 3720, 0, Now.AddDays(-365))]);
        repository.UpsertSchema(435150,
            [new AchievementSchema("D_1", "Rise", "desc", "i", "g", false, 0)], Now);

        var row = new SqliteLibraryQuery(connection).GetQueue(Now).Rows.Single(r => r.AppId == 435150);

        Assert.True(row.RarityUnknown);
        Assert.Equal("1 left, rarity unknown for all of them", row.Reason);
    }

    [Fact]
    public void ExcludesAGameWhoseSchemaIsCachedButIsNoLongerOwned()
    {
        using var connection = Seed();
        // A schema can legitimately be cached for a game the library no
        // longer contains: Steam schema fetches are not re-verified against
        // current ownership. Blending an unowned game into the completion
        // queue would rank a game the player cannot even open, which makes
        // the ranking meaningless — so GetQueue must exclude it.
        new GameRepository(connection).UpsertSchema(435150,
            [new AchievementSchema("D_1", "Rise", "desc", "i", "g", false, 0)], Now);

        var queue = new SqliteLibraryQuery(connection).GetQueue(Now);

        Assert.DoesNotContain(queue.Rows, r => r.AppId == 435150);
    }

    [Fact]
    public void SummarisesTheLibrary()
    {
        using var connection = Seed();
        connection.Execute(
            "INSERT INTO settings (id, last_full_sync_at) VALUES (1, @At)",
            new { At = Now.AddMinutes(-14).ToString("o") });

        var summary = new SqliteLibraryQuery(connection).GetSummary(Now);

        Assert.Equal(3, summary.GameCount);
        Assert.Equal(3, summary.AchievementCount);
        Assert.Equal("3 games · 3 ach.", summary.CountsText);
        Assert.Equal("Last sync 14 min ago", summary.LastSyncText);
    }

    [Fact]
    public void SaysSoWhenNothingHasEverBeenSynced()
    {
        using var connection = Seed();

        Assert.Equal("Never synced", new SqliteLibraryQuery(connection).GetSummary(Now).LastSyncText);
    }

    [Fact]
    public void ReadsSyncHistoryMostRecentFirst()
    {
        using var connection = Seed();
        connection.Execute("""
            INSERT INTO sync_runs (started_at, kind, games_synced, duration_ms) VALUES
                (@Older, 'full', 1482, 531000),
                (@Newer, 'incremental', 4, 2149);
            """,
            new
            {
                Older = Now.AddDays(-4).ToString("o"),
                Newer = Now.AddHours(-2).ToString("o"),
            });

        var history = new SqliteLibraryQuery(connection).GetSyncHistory(10, Now);

        Assert.Equal("Incremental — 4 games changed", history[0].WhatText);
        Assert.Equal("2.1 s", history[0].DurationText);
        // Composed rather than written out: Formatting.Number separates
        // thousands with a thin space (U+2009), and a literal ASCII space here
        // would fail the comparison for a reason invisible in the diff.
        Assert.Equal($"Full sync — {Formatting.Number(1482)} games", history[1].WhatText);
        Assert.Equal("8 min 51 s", history[1].DurationText);
    }

    [Fact]
    public void ReportsAnEmptyHistoryRatherThanInventingRows()
    {
        using var connection = Seed();

        Assert.Empty(new SqliteLibraryQuery(connection).GetSyncHistory(10, Now));
    }
}
