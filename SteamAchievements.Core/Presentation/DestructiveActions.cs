namespace SteamAchievements.Core.Presentation;

/// <summary>
/// Whether a destructive action may be offered, and the copy to show instead
/// when it may not. <c>BlockedReason</c> is null exactly when
/// <c>Allowed</c> is true — a blocked gate with nothing to say renders an empty
/// space where the button was.
/// </summary>
public sealed record DestructiveActionGate(bool Allowed, string? BlockedReason);

/// <summary>
/// The gate in front of "Change account" and "Reset database".
///
/// It exists because the failure it prevents is silent. Both actions empty the
/// tables, and a sync that is still running keeps writing the <em>old</em>
/// account's games afterwards through the writer connection. What is left is a
/// database whose stored SteamID64 names one account and whose rows are partly
/// another — no table records which account a row came from, so nothing later
/// can notice, and the ranking is simply wrong.
///
/// Only <see cref="SyncPhase.Running"/> blocks: that is the one phase with a
/// run in flight. <see cref="SyncPhase.Paused"/> and
/// <see cref="SyncPhase.CircuitOpen"/> have no writer — <c>SyncCoordinator.Pause</c>
/// cancels the run and publishes <c>Paused</c> only once it has unwound — so
/// blocking them would refuse a safe action.
///
/// In Core, and not an <c>if</c> inside the settings screen, for the reason
/// section 3.1 of the design gives: a decision taken in a <c>.razor</c> file is
/// verified by pushing to CI and running the artifact on Windows.
/// </summary>
public static class DestructiveActions
{
    public const string SyncRunning =
        "A sync is running. Stop or pause it first — emptying the library now would leave "
        + "the old account's games behind, mixed into the new one.";

    public static DestructiveActionGate For(SyncStatusView status) =>
        status.Phase == SyncPhase.Running
            ? new DestructiveActionGate(false, SyncRunning)
            : new DestructiveActionGate(true, null);
}
