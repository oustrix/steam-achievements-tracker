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
    private bool _pausing;
    private SyncStatusView _status;
    private Task _completion = Task.CompletedTask;

    public SyncCoordinator(
        ISyncRunner runner, IAccountStore accounts, SyncJournal journal, Func<DateTimeOffset> now)
    {
        _runner = runner;
        _accounts = accounts;
        _journal = journal;
        _now = now;

        _status = _accounts.KeyRejectedAt is not null
            ? new SyncStatusView(SyncPhase.KeyRejected, 0, 0, string.Empty,
                "Steam rejected the API key", "Replace it in settings to continue.", null)
            : _journal.LastSyncedAt is null
                ? new SyncStatusView(SyncPhase.NeverRun, 0, 0, string.Empty, "Never synced", null, null)
                : new SyncStatusView(SyncPhase.Idle, 0, 0, string.Empty, "Up to date", null, null);
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
        StoredAccount? account;

        lock (_gate)
        {
            if (_status.Phase == SyncPhase.Running)
            {
                return;
            }

            account = _accounts.Current;

            if (account is not null)
            {
                _pausing = false;
                _cancellation = new CancellationTokenSource();
                _status = new SyncStatusView(SyncPhase.Running, 0, 0, string.Empty, "Starting…", null, null);
                _completion = RunAsync(account.SteamId64, force, _cancellation.Token);
            }
        }

        if (account is null)
        {
            Publish(new SyncStatusView(SyncPhase.Failed, 0, 0, string.Empty,
                "Sync failed", null, "No Steam account is configured."));
            return;
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

            Publish(new SyncStatusView(
                SyncPhase.Running, report.Completed, report.Total, report.CurrentGame,
                $"Syncing {report.Completed} of {report.Total}", report.CurrentGame, null));
        });

        try
        {
            await _runner.RunAsync(steamId, force, progress, cancellationToken);

            var finishedAt = _now();
            _accounts.ClearKeyRejected();
            _journal.RecordRun(new SyncRunRecord(startedAt, kind, completed, Elapsed(startedAt, finishedAt), null));
            _journal.MarkSyncCompleted(finishedAt);

            Publish(new SyncStatusView(SyncPhase.Idle, completed, total, string.Empty, "Up to date", null, null));
        }
        catch (OperationCanceledException)
        {
            var paused = Paused();
            _journal.RecordRun(new SyncRunRecord(
                startedAt, kind, completed, Elapsed(startedAt, _now()), SyncJournal.Cancelled));

            Publish(new SyncStatusView(
                paused ? SyncPhase.Paused : SyncPhase.Idle, completed, total, string.Empty,
                paused ? $"Paused at {completed} of {total}" : "Sync cancelled", null, null));
        }
        catch (SteamApiException e) when (e.Kind == SteamApiErrorKind.InvalidKey)
        {
            var finishedAt = _now();
            _accounts.MarkKeyRejected(finishedAt);
            _journal.RecordRun(new SyncRunRecord(startedAt, kind, completed, Elapsed(startedAt, finishedAt), e.Message));

            Publish(new SyncStatusView(
                SyncPhase.KeyRejected, completed, total, string.Empty,
                "Steam rejected the API key", "Replace it in settings to continue.", e.Message));
        }
        catch (Exception e)
        {
            _journal.RecordRun(new SyncRunRecord(
                startedAt, kind, completed, Elapsed(startedAt, _now()), e.Message));

            Publish(new SyncStatusView(
                SyncPhase.Failed, completed, total, string.Empty, "Sync failed", null, e.Message));
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

    private bool Paused()
    {
        lock (_gate)
        {
            return _pausing;
        }
    }

    private static long Elapsed(DateTimeOffset from, DateTimeOffset to) => (long)(to - from).TotalMilliseconds;

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
