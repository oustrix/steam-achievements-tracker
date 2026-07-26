using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Sync;

public sealed record SyncWorkItem(uint AppId, int Playtime, bool NeedSchema, bool NeedGlobal, bool NeedPlayer);

/// <summary>
/// Decides what actually needs fetching. Pure function of current library
/// state and previous sync state — no I/O, no clock.
/// </summary>
public static class SyncPlanner
{
    public static IReadOnlyList<SyncWorkItem> Plan(
        IReadOnlyList<OwnedGame> owned,
        IReadOnlyDictionary<uint, GameSyncState> states,
        DateTimeOffset now,
        SyncOptions options,
        bool force)
    {
        var items = new List<SyncWorkItem>();

        foreach (var game in owned.OrderByDescending(g => g.PlaytimeForever))
        {
            states.TryGetValue(game.AppId, out var state);

            // Once Steam says an app has no stats, that never changes.
            // Re-asking would burn hundreds of requests per sync.
            if (state is { HasAchievements: false })
            {
                continue;
            }

            // Schema and global percentages are refreshed off their own TTLs,
            // not off whether the game ever completed a *full* sync. Keying
            // them off "never synced" meant a game whose player-achievements
            // call failed after schema and global both succeeded (rate
            // limit, transient 5xx, circuit breaker) would permanently
            // re-fetch schema and global on every later run even though both
            // are still well inside their TTLs. A never-encountered game has
            // null timestamps, so it still gets everything on first
            // encounter — the null case already covers what the old
            // "never synced" flag was standing in for.
            var needSchema = force || state is null || state.SchemaSyncedAt is null
                || now - state.SchemaSyncedAt.Value > options.SchemaTtl;

            var needGlobal = force || state is null || state.GlobalSyncedAt is null
                || now - state.GlobalSyncedAt.Value > options.GlobalTtl;

            // The core optimization: unchanged playtime means unchanged
            // achievements. A negative SyncedPlaytime means the
            // player-achievements call never completed for this game, so it
            // must be retried regardless of playtime.
            var needPlayer = force || state is null || state.SyncedPlaytime < 0
                || game.PlaytimeForever != state.SyncedPlaytime;

            if (needSchema || needGlobal || needPlayer)
            {
                items.Add(new SyncWorkItem(game.AppId, game.PlaytimeForever, needSchema, needGlobal, needPlayer));
            }
        }

        return items;
    }
}
