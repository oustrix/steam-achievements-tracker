namespace SteamAchievements.Core.Presentation;

/// <summary>
/// What the sync card draws. <c>ShowCancel</c> rather than an enabled flag:
/// cancelling something that is not running is not a disabled action, it is not
/// an action.
/// </summary>
public sealed record SyncControlsView(
    string Title, string PrimaryLabel, bool PrimaryEnabled, bool ShowCancel);

/// <summary>
/// The whole "state → buttons" table, in Core so it is under dotnet test.
///
/// There is no Force here. The primary button is always
/// <c>Start(force: false)</c>; a full resync is its own button and needs no
/// lookup to know what it does.
/// </summary>
public static class SyncControls
{
    public static SyncControlsView For(SyncStatusView status) => status.Phase switch
    {
        SyncPhase.Running => new SyncControlsView("Sync in progress", "Pause", true, true),

        SyncPhase.Paused => new SyncControlsView("Sync paused", "Resume", true, false),

        SyncPhase.CircuitOpen => new SyncControlsView("Sync waiting to retry", "Sync", false, false),

        _ => new SyncControlsView("No sync running", "Sync", CanStart(status.Problem), false),
    };

    // A blocked library is not a library that benefits from another attempt.
    // A private profile is: the setting may have been changed a moment ago.
    private static bool CanStart(SyncProblem problem) => problem switch
    {
        SyncProblem.InvalidKey or SyncProblem.OtherAccount => false,
        _ => true,
    };
}
