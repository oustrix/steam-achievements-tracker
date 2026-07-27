namespace SteamAchievements.Core.Presentation;

/// <summary>
/// One answer to "the stored library may have changed, re-read it".
///
/// Three screens need it — the queue, the sidebar's summary card and the sync
/// screen's history — and three copies of a "was it running" flag drift. It is
/// in Core rather than in the UI project because it is logic, and logic in the
/// UI project is verified by pushing to CI and running the artifact on Windows.
///
/// It fires on the trailing edge of a run rather than on progress: reports
/// arrive about five times a second and re-reading the queue re-ranks the whole
/// library.
///
/// It does not marshal onto a render thread. That obligation stays with the
/// components, where it belongs — see the host design section 5.3 — because a
/// dispatcher in Core would hide the rule from the only code that can obey it.
/// </summary>
public sealed class LibraryChangeSignal : IDisposable
{
    private readonly ISyncPresenter _sync;
    private readonly IAccountAdmin _accounts;
    private readonly Lock _gate = new();

    private bool _wasRunning;

    public LibraryChangeSignal(ISyncPresenter sync, IAccountAdmin accounts)
    {
        _sync = sync;
        _accounts = accounts;
        _wasRunning = sync.Status.Phase == SyncPhase.Running;

        _sync.Changed += OnSyncChanged;
        _accounts.Changed += OnAccountsChanged;
    }

    public event Action? Changed;

    private void OnSyncChanged()
    {
        bool stopped;

        // The edge is computed under the lock because ISyncPresenter.Changed
        // arrives on the sync engine's worker thread while Start raises it on
        // the caller's; two threads deciding "was it running" from one field is
        // how a run ends without anybody re-reading the library. Raising happens
        // outside, so a handler that reads Status cannot re-enter.
        lock (_gate)
        {
            var running = _sync.Status.Phase == SyncPhase.Running;
            stopped = _wasRunning && !running;
            _wasRunning = running;
        }

        if (stopped)
        {
            Changed?.Invoke();
        }
    }

    private void OnAccountsChanged() => Changed?.Invoke();

    public void Dispose()
    {
        _sync.Changed -= OnSyncChanged;
        _accounts.Changed -= OnAccountsChanged;
    }
}
