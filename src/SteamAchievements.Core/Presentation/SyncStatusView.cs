namespace SteamAchievements.Core.Presentation;

/// <summary>
/// What the sync is doing right now.
///
/// <c>Paused</c> and <c>Idle</c> are both "not running" — the difference is
/// whether the user stopped it intending to come back, which decides whether
/// the button says "Resume" or "Sync".
/// </summary>
public enum SyncPhase
{
    Idle,
    Running,
    Paused,
    CircuitOpen,
}

/// <summary>
/// A condition that stops the library from being synced, as opposed to
/// something that went wrong during a run. Each value names one of the Notice
/// blocks the sync screen renders. It is stated explicitly because it cannot be
/// inferred: an empty library means a rejected key, a private profile, a
/// different account signed in, or simply that nobody has synced yet — which is
/// the state every new installation starts in.
///
/// Kept apart from <see cref="SyncPhase"/> on purpose. "What is happening" and
/// "what is blocking" are orthogonal: a rejected key leaves the sync idle
/// <em>and</em> blocked, and folding the two into one enum forces every future
/// state to pick a side it does not belong on.
/// </summary>
public enum SyncProblem
{
    None,
    InvalidKey,
    PrivateProfile,
    OtherAccount,
}

public sealed record SyncStatusView(
    SyncPhase Phase,
    int Completed,
    int Total,
    string CurrentGame,
    string EtaText,
    string RateText,
    string? AlertTitle,
    string? AlertBody,
    SyncProblem Problem = SyncProblem.None)
{
    public static SyncStatusView Idle { get; } =
        new(SyncPhase.Idle, 0, 0, "", "", "", null, null);

    public int Percent => Total == 0 ? 0 : (int)Math.Round(100.0 * Completed / Total);
}

/// <summary>
/// What the sync screen and the sidebar card read.
///
/// <c>Changed</c> is raised from whichever thread the sync engine's progress
/// callback runs on, which is a worker thread, not the renderer. Components
/// must wrap their reaction in <c>InvokeAsync(StateHasChanged)</c>: reading
/// <c>ILibraryQuery</c> off the render thread is concurrent use of a single
/// SqliteConnection, which corrupts rather than throws.
/// </summary>
public interface ISyncPresenter
{
    SyncStatusView Status { get; }

    event Action? Changed;
}

/// <summary>
/// There is no <c>Resume</c>. Resuming is <c>Start(force: false)</c> — the sync
/// is resumable because progress is written per game — and the screen picks the
/// button's label from <see cref="SyncStatusView.Phase"/>.
/// </summary>
public interface ISyncController
{
    void Start(bool force);

    void Pause();

    void Cancel();
}
