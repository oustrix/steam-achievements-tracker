using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Sync;

namespace SteamAchievements.Core.Tests.Sync;

public class SyncPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    private static OwnedGame Game(uint appId, int playtime) =>
        new(appId, $"Game {appId}", "icon", playtime, 0, null);

    private static GameSyncState State(uint appId, int syncedPlaytime, bool hasAchievements = true,
        DateTimeOffset? schemaAt = null, DateTimeOffset? globalAt = null) =>
        new(appId, hasAchievements, syncedPlaytime, schemaAt ?? Now, globalAt ?? Now, Now);

    [Fact]
    public void PlansEverythingForNeverSyncedGame()
    {
        var plan = SyncPlanner.Plan([Game(292030, 100)], new Dictionary<uint, GameSyncState>(),
            Now, SyncOptions.Default, force: false);

        var item = Assert.Single(plan);
        Assert.True(item.NeedSchema);
        Assert.True(item.NeedGlobal);
        Assert.True(item.NeedPlayer);
    }

    [Fact]
    public void SkipsGameWhosePlaytimeDidNotChange()
    {
        var plan = SyncPlanner.Plan(
            [Game(292030, 100)],
            new Dictionary<uint, GameSyncState> { [292030] = State(292030, syncedPlaytime: 100) },
            Now, SyncOptions.Default, force: false);

        Assert.Empty(plan);
    }

    [Fact]
    public void RequestsPlayerProgressWhenPlaytimeIncreased()
    {
        var plan = SyncPlanner.Plan(
            [Game(292030, 160)],
            new Dictionary<uint, GameSyncState> { [292030] = State(292030, syncedPlaytime: 100) },
            Now, SyncOptions.Default, force: false);

        var item = Assert.Single(plan);
        Assert.True(item.NeedPlayer);
        Assert.False(item.NeedSchema);
    }

    [Fact]
    public void ExcludesGamesKnownToHaveNoAchievements()
    {
        var plan = SyncPlanner.Plan(
            [Game(220, 500)],
            new Dictionary<uint, GameSyncState> { [220] = State(220, syncedPlaytime: 100, hasAchievements: false) },
            Now, SyncOptions.Default, force: false);

        Assert.Empty(plan);
    }

    [Fact]
    public void RefreshesSchemaAfterTtlExpires()
    {
        var plan = SyncPlanner.Plan(
            [Game(292030, 100)],
            new Dictionary<uint, GameSyncState>
            {
                [292030] = State(292030, syncedPlaytime: 100, schemaAt: Now.AddDays(-31)),
            },
            Now, SyncOptions.Default, force: false);

        var item = Assert.Single(plan);
        Assert.True(item.NeedSchema);
        Assert.False(item.NeedPlayer);
    }

    [Fact]
    public void RefreshesGlobalPercentagesAfterTtlExpires()
    {
        var plan = SyncPlanner.Plan(
            [Game(292030, 100)],
            new Dictionary<uint, GameSyncState>
            {
                [292030] = State(292030, syncedPlaytime: 100, globalAt: Now.AddDays(-8)),
            },
            Now, SyncOptions.Default, force: false);

        Assert.True(Assert.Single(plan).NeedGlobal);
    }

    [Fact]
    public void ForceRequestsEverythingForEveryGameWithAchievements()
    {
        var plan = SyncPlanner.Plan(
            [Game(292030, 100), Game(220, 0)],
            new Dictionary<uint, GameSyncState>
            {
                [292030] = State(292030, syncedPlaytime: 100),
                [220] = State(220, syncedPlaytime: 0, hasAchievements: false),
            },
            Now, SyncOptions.Default, force: true);

        // Force refreshes everything we can, but still respects the permanent
        // "this app has no stats" fact — re-asking would waste hundreds of calls.
        var item = Assert.Single(plan);
        Assert.Equal(292030u, item.AppId);
        Assert.True(item is { NeedSchema: true, NeedGlobal: true, NeedPlayer: true });
    }

    [Fact]
    public void OrdersRecentlyPlayedGamesFirst()
    {
        var plan = SyncPlanner.Plan(
            [Game(1, 10), Game(2, 5000), Game(3, 200)],
            new Dictionary<uint, GameSyncState>(),
            Now, SyncOptions.Default, force: false);

        Assert.Equal([2u, 3u, 1u], plan.Select(i => i.AppId));
    }
}
