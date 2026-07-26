namespace SteamAchievements.Core.Presentation;

/// <summary>
/// Every distinction the screens need about the sync, and no more.
///
/// <c>Paused</c> and <c>Idle</c> are both "not running" — the difference is
/// whether the user stopped it intending to come back, which decides whether the
/// button says "Resume" or "Sync". <c>KeyRejected</c> is kept apart from
/// <c>Failed</c> because it is the one failure with an action attached to it.
/// </summary>
public enum SyncPhase
{
    NeverRun,
    Idle,
    Running,
    Paused,
    Failed,
    KeyRejected,
}

public sealed record SyncStatusView(
    SyncPhase Phase,
    int Completed,
    int Total,
    string CurrentGame,
    string Headline,
    string? Detail,
    string? Error);

/// <summary>
/// What the sidebar card and the sync screen read.
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
