using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Sync;

namespace SteamAchievements.Core.App;

/// <summary>
/// Owns everything about a sync that is not the sync itself: which phase the UI
/// is in, the cancellation token, the journal row, and the key-rejection flag.
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
    private readonly Lock _gate = new();

    private CancellationTokenSource? _cancellation;

    // Written under _gate, read once from the run's cancellation handler.
    // Volatile rather than lock-guarded because a single bool needs nothing
    // more than visibility.
    private volatile bool _pausing;

    private SyncStatusView _status;
    private Task _completion = Task.CompletedTask;

    public SyncCoordinator(
        ISyncRunner runner, IAccountStore accounts, SyncJournal journal, Func<DateTimeOffset> now)
    {
        _runner = runner;
        _accounts = accounts;
        _journal = journal;
        _now = now;

        _status =
            _accounts.KeyRejectedAt is not null ? SyncStatusView.KeyRejected()
            : _journal.LastSyncedAt is null ? SyncStatusView.NeverRun()
            : SyncStatusView.Idle();
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
            Publish(SyncStatusView.Failed(0, 0, "No Steam account is configured."));
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
            _status = SyncStatusView.Starting();
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

            Publish(SyncStatusView.Running(report.Completed, report.Total, report.CurrentGame));
        });

        try
        {
            await _runner.RunAsync(steamId, force, progress, cancellationToken);

            var finishedAt = _now();
            _accounts.ClearKeyRejected();
            Record(startedAt, finishedAt, kind, completed, error: null);
            _journal.MarkSyncCompleted(finishedAt);

            Publish(SyncStatusView.Idle(completed, total));
        }
        catch (OperationCanceledException)
        {
            Record(startedAt, _now(), kind, completed, SyncJournal.Cancelled);

            Publish(_pausing
                ? SyncStatusView.Paused(completed, total)
                : SyncStatusView.Cancelled(completed, total));
        }
        catch (SteamApiException e) when (e.Kind == SteamApiErrorKind.InvalidKey)
        {
            var finishedAt = _now();
            _accounts.MarkKeyRejected(finishedAt);
            Record(startedAt, finishedAt, kind, completed, e.Message);

            Publish(SyncStatusView.KeyRejected(completed, total, e.Message));
        }
        catch (Exception e)
        {
            Record(startedAt, _now(), kind, completed, e.Message);

            Publish(SyncStatusView.Failed(completed, total, e.Message));
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

        // Bounded rather than indefinite: shutdown must not hang on a sync that
        // refuses to notice its cancellation.
        Completion.Wait(TimeSpan.FromSeconds(5));

        lock (_gate)
        {
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }
}
