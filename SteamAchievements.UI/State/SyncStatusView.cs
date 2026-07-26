namespace SteamAchievements.UI.State;

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
/// blocks spec section 7 places on the sync screen. It is stated explicitly
/// because it cannot be inferred: an empty library means a rejected key, a
/// private profile, a different account signed in, or simply that nobody has
/// synced yet — which is the state every new installation starts in.
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
/// What the sync screen renders. Deliberately a seam rather than a direct
/// dependency on SyncOrchestrator: wiring the two together — real progress,
/// pause, cancel, rate and ETA — is the next spec's work, and this lets the
/// screen be finished and verified before that exists.
/// </summary>
public interface ISyncPresenter
{
    SyncStatusView Status { get; }
}

/// <summary>The production implementation until the sync spec replaces it.</summary>
public sealed class IdleSyncPresenter : ISyncPresenter
{
    public SyncStatusView Status => SyncStatusView.Idle;
}
