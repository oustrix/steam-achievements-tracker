using Microsoft.Extensions.Logging;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Sync;

namespace SteamAchievements.Core.App;

/// <summary>
/// Owns everything about a sync that is not the sync itself: what the screen
/// shows, the cancellation token, the journal row, and the key-rejection flag.
///
/// Deliberately depends on <see cref="ISyncRunner"/> rather than on
/// <c>SyncOrchestrator</c>, so every branch below is reachable from a unit test.
/// </summary>
public sealed class SyncCoordinator : ISyncPresenter, ISyncController, IDisposable
{
    /// <summary>
    /// Invokes the handler on the calling thread. <see cref="Progress{T}"/>
    /// captures the SynchronizationContext at construction and posts
    /// asynchronously, which would make the recorded <c>games_synced</c> lag
    /// behind the run it belongs to.
    /// </summary>
    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public InlineProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }

    private readonly ISyncRunner _runner;
    private readonly IAccountStore _accounts;
    private readonly SyncJournal _journal;
    private readonly Func<DateTimeOffset> _now;
    private readonly ILogger<SyncCoordinator> _log;
    private readonly Lock _gate = new();

    private CancellationTokenSource? _cancellation;

    // Written under _gate, read from the run's cancellation handler. Volatile
    // rather than lock-guarded because a single bool needs nothing more than
    // visibility.
    private volatile bool _pausing;

    private SyncStatusView _status;
    private Task _completion = Task.CompletedTask;

    public SyncCoordinator(
        ISyncRunner runner,
        IAccountStore accounts,
        SyncJournal journal,
        Func<DateTimeOffset> now,
        ILogger<SyncCoordinator> log)
    {
        _runner = runner;
        _accounts = accounts;
        _journal = journal;
        _now = now;
        _log = log;

        // A key rejected during an earlier session is still rejected now. The
        // flag is persisted precisely so a restart does not hide it and spend
        // requests rediscovering it.
        _status = _accounts.KeyRejectedAt is not null
            ? SyncStatusView.Idle with { Problem = SyncProblem.InvalidKey }
            : SyncStatusView.Idle;

        if (_accounts.KeyRejectedAt is not null)
        {
            _log.LogWarning(
                "starting with a key Steam rejected at {RejectedAt}; syncing is blocked until it is replaced",
                _accounts.KeyRejectedAt);
        }
    }

    public SyncStatusView Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public event Action? Changed;

    /// <summary>
    /// Completes when the in-flight run does, or immediately when none is. Public
    /// because the host has to await it during shutdown: disposing the SQLite
    /// connections while the orchestrator is still writing to them is a use of a
    /// disposed connection from a worker thread.
    /// </summary>
    public Task Completion
    {
        get
        {
            lock (_gate)
            {
                return _completion;
            }
        }
    }

    public void Start(bool force)
    {
        // Read outside the lock: this is a SQLite query, and nothing else in
        // this class touches the account store under _gate.
        var account = _accounts.Current;

        if (account is null)
        {
            Publish(SyncStatusView.Idle with
            {
                AlertTitle = "No Steam account is configured",
                AlertBody = "Finish onboarding before syncing.",
            });
            _log.LogWarning("sync requested with no Steam account configured");
            return;
        }

        lock (_gate)
        {
            if (_status.Phase == SyncPhase.Running)
            {
                return;
            }

            _pausing = false;
            _cancellation = new CancellationTokenSource();
            _status = SyncStatusView.Idle with { Phase = SyncPhase.Running };
            _log.LogInformation(
                "sync started steam_id={SteamId} force={Force}", account.SteamId64, force);
            _completion = RunAsync(account.SteamId64, force, _cancellation.Token);
        }

        Changed?.Invoke();
    }

    public void Pause() => Stop(pausing: true);

    public void Cancel() => Stop(pausing: false);

    private void Stop(bool pausing)
    {
        lock (_gate)
        {
            if (_status.Phase != SyncPhase.Running)
            {
                return;
            }

            _pausing = pausing;
            _cancellation?.Cancel();
            _log.LogInformation("sync {Action} requested", pausing ? "pause" : "cancel");
        }
    }

    private async Task RunAsync(ulong steamId, bool force, CancellationToken cancellationToken)
    {
        var startedAt = _now();
        var kind = force ? "full" : "incremental";
        var completed = 0;
        var total = 0;

        var progress = new InlineProgress<SyncProgress>(report =>
        {
            completed = report.Completed;
            total = report.Total;

            var elapsed = _now() - startedAt;

            Publish(new SyncStatusView(
                SyncPhase.Running, report.Completed, report.Total, report.CurrentGame,
                SyncProgressReport.Eta(report.Completed, report.Total, elapsed),
                SyncProgressReport.Rate(report.Completed, elapsed),
                null, null));
        });

        try
        {
            await _runner.RunAsync(steamId, force, progress, cancellationToken);

            var finishedAt = _now();
            _accounts.ClearKeyRejected();
            Record(startedAt, finishedAt, kind, completed, error: null);
            _journal.MarkSyncCompleted(finishedAt);
            _log.LogInformation(
                "sync completed games={Completed} in {Elapsed}ms",
                completed, (long)(finishedAt - startedAt).TotalMilliseconds);

            Publish(SyncStatusView.Idle);
        }
        catch (OperationCanceledException)
        {
            Record(startedAt, _now(), kind, completed, SyncRunView.CancelledMarker);
            _log.LogInformation(
                "sync {Outcome} after {Completed} of {Total} games",
                _pausing ? "paused" : "cancelled", completed, total);

            // Paused keeps the figures on screen so it is clear where to resume
            // from; cancelled returns to a clean idle.
            Publish(_pausing
                ? SyncStatusView.Idle with
                {
                    Phase = SyncPhase.Paused,
                    Completed = completed,
                    Total = total,
                }
                : SyncStatusView.Idle);
        }
        catch (SteamApiException e) when (e.Kind == SteamApiErrorKind.InvalidKey)
        {
            var finishedAt = _now();
            _accounts.MarkKeyRejected(finishedAt);
            Record(startedAt, finishedAt, kind, completed, e.Message);
            _log.LogError(e, "sync stopped: Steam rejected the API key");

            // A rejected key is a blocking condition rather than a failed run:
            // retrying changes nothing until the key is replaced, and the screen
            // renders that as its own Notice with a link to settings.
            Publish(SyncStatusView.Idle with
            {
                Completed = completed,
                Total = total,
                Problem = SyncProblem.InvalidKey,
            });
        }
        catch (Exception e)
        {
            Record(startedAt, _now(), kind, completed, e.Message);
            _log.LogError(e, "sync failed after {Completed} of {Total} games", completed, total);

            Publish(SyncStatusView.Idle with
            {
                Completed = completed,
                Total = total,
                AlertTitle = "Sync failed",
                AlertBody = e.Message,
            });
        }
        finally
        {
            lock (_gate)
            {
                _cancellation?.Dispose();
                _cancellation = null;
            }
        }
    }

    private void Record(DateTimeOffset startedAt, DateTimeOffset finishedAt, string kind, int completed, string? error) =>
        _journal.RecordRun(new SyncRunRecord(
            startedAt, kind, completed, (long)(finishedAt - startedAt).TotalMilliseconds, error));

    /// <summary>
    /// Assigns under the lock and raises outside it. Raising while holding the
    /// lock would let a handler that reads <see cref="Status"/> re-enter it.
    /// </summary>
    private void Publish(SyncStatusView status)
    {
        lock (_gate)
        {
            _status = status;
        }

        Changed?.Invoke();
    }

    public void Dispose()
    {
        Cancel();

        if (!Completion.Wait(TimeSpan.FromSeconds(5)))
        {
            // Shutdown must not hang on a sync that refuses to notice its
            // cancellation — but a timeout here means the orchestrator's
            // workers are still live when the host closes the connections
            // they write through, which is worth knowing about afterwards.
            _log.LogError("shutdown timed out waiting five seconds for the sync to stop");
        }

        lock (_gate)
        {
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }
}
