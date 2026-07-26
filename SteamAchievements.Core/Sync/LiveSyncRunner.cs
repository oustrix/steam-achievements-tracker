using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Sync;

/// <summary>
/// Builds a <see cref="SyncOrchestrator"/> per run out of whatever key is
/// currently stored. Per run rather than once, because the user can replace the
/// key in settings and the next sync has to use the new one without restarting
/// the application.
/// </summary>
public sealed class LiveSyncRunner : ISyncRunner
{
    private readonly ISecretStore _secrets;
    private readonly GameRepository _repository;
    private readonly Func<string, SteamApiClient> _clientFactory;

    public LiveSyncRunner(
        ISecretStore secrets, GameRepository repository, Func<string, SteamApiClient> clientFactory)
    {
        _secrets = secrets;
        _repository = repository;
        _clientFactory = clientFactory;
    }

    public async Task RunAsync(
        ulong steamId, bool force, IProgress<SyncProgress>? progress, CancellationToken cancellationToken)
    {
        var key = _secrets.Read();

        if (string.IsNullOrEmpty(key))
        {
            // Reported as InvalidKey rather than as its own kind so the state
            // machine lands on KeyRejected, which is the screen that lets the
            // user do something about it.
            throw new SteamApiException(
                SteamApiErrorKind.InvalidKey, 0, "No Steam API key is stored. Add one in settings.");
        }

        var orchestrator = new SyncOrchestrator(_clientFactory(key), _repository, SyncOptions.Default);

        await orchestrator.RunAsync(steamId, force, progress, cancellationToken);
    }
}
