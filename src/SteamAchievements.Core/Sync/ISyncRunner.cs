namespace SteamAchievements.Core.Sync;

/// <summary>
/// One sync, start to finish. The seam exists so the state machine around it can
/// be tested against scripted progress and scripted failures instead of against
/// a real orchestrator, an HTTP fixture and a database.
/// </summary>
public interface ISyncRunner
{
    Task RunAsync(
        ulong steamId,
        bool force,
        IProgress<SyncProgress>? progress,
        CancellationToken cancellationToken);
}
