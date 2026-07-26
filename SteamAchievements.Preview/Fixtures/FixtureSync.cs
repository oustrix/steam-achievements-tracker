using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>
/// Both sync seams, the way SyncCoordinator implements both in the real host.
///
/// It genuinely runs: Start advances a timer-driven counter to the end and then
/// publishes Idle. Without a real phase transition there is no way to check on
/// macOS that leaving Running actually refreshes the queue and the sidebar,
/// which is the part of this feature most likely to be wrong.
///
/// The timer fires on the thread pool, so the Changed handlers land off the
/// render thread exactly as they do in production.
/// </summary>
public sealed class FixtureSync : ISyncPresenter, ISyncController, IDisposable
{
    private const int Total = 1482;
    private const int GamesPerTick = 60;
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(120);

    private static readonly string[] Games =
    [
        "Divinity: Original Sin 2", "Hollow Knight", "Slay the Spire",
        "Factorio", "Hades", "Return of the Obra Dinn",
    ];

    private readonly FixtureLibraryQuery _library;
    private readonly Lock _gate = new();

    private Timer? _timer;
    private SyncStatusView? _live;
    private int _completed;

    public FixtureSync(FixtureLibraryQuery library) => _library = library;

    public SyncStatusView Status
    {
        get
        {
            lock (_gate)
            {
                return _live ?? Scenario;
            }
        }
    }

    public event Action? Changed;

    /// <summary>
    /// The states a scenario asks for, shown until somebody presses a button.
    /// After that the fixture's own run is the truth.
    /// </summary>
    private SyncStatusView Scenario => _library.Scenario switch
    {
        Fixtures.Scenario.InvalidKey => SyncStatusView.Idle with { Problem = SyncProblem.InvalidKey },
        Fixtures.Scenario.PrivateProfile => SyncStatusView.Idle with { Problem = SyncProblem.PrivateProfile },
        Fixtures.Scenario.OtherAccount => SyncStatusView.Idle with { Problem = SyncProblem.OtherAccount },

        Fixtures.Scenario.CircuitOpen => new SyncStatusView(
            SyncPhase.CircuitOpen, 412, Total, "Divinity: Original Sin 2 — schema",
            "~6 min left", "4.8 req/s",
            "Paused after five consecutive failures",
            "Steam returned 429 five times in a row. Waiting 8 s before the next attempt."),

        _ => SyncStatusView.Idle,
    };

    public void Start(bool force)
    {
        lock (_gate)
        {
            if (_live?.Phase == SyncPhase.Running)
            {
                return;
            }

            // Resuming keeps the count; a full resync starts over.
            _completed = force ? 0 : _completed;
            _timer ??= new Timer(_ => Advance(), null, Tick, Tick);
        }

        Advance();
    }

    public void Pause() => Stop(SyncPhase.Paused);

    public void Cancel() => Stop(SyncPhase.Idle);

    private void Stop(SyncPhase phase)
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;

            _live = phase == SyncPhase.Paused
                ? SyncStatusView.Idle with { Phase = phase, Completed = _completed, Total = Total }
                : SyncStatusView.Idle;

            if (phase == SyncPhase.Idle)
            {
                _completed = 0;
            }
        }

        Changed?.Invoke();
    }

    private void Advance()
    {
        lock (_gate)
        {
            _completed = Math.Min(Total, _completed + GamesPerTick);

            if (_completed >= Total)
            {
                _timer?.Dispose();
                _timer = null;

                // A finished run makes the library non-empty again, which is
                // what the queue and the sidebar are meant to notice.
                _library.Cleared = false;
                _live = SyncStatusView.Idle;
            }
            else
            {
                _live = new SyncStatusView(
                    SyncPhase.Running, _completed, Total,
                    $"{Games[_completed / GamesPerTick % Games.Length]} — achievements",
                    "~6 min left", "4.8 req/s", null, null);
            }
        }

        Changed?.Invoke();
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
