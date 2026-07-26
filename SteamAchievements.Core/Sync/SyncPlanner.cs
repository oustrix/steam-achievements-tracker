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

            var neverSynced = state is null || state.SyncedPlaytime < 0;

            var needSchema = force || neverSynced || state!.SchemaSyncedAt is null
                || now - state.SchemaSyncedAt.Value > options.SchemaTtl;

            var needGlobal = force || neverSynced || state!.GlobalSyncedAt is null
                || now - state.GlobalSyncedAt.Value > options.GlobalTtl;

            // The core optimization: unchanged playtime means unchanged achievements.
            var needPlayer = force || neverSynced || game.PlaytimeForever != state!.SyncedPlaytime;

            if (needSchema || needGlobal || needPlayer)
            {
                items.Add(new SyncWorkItem(game.AppId, game.PlaytimeForever, needSchema, needGlobal, needPlayer));
            }
        }

        return items;
    }
}
