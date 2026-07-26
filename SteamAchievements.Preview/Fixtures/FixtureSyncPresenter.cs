using SteamAchievements.UI.State;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>Shows the sync screen under each state without running a sync.</summary>
public sealed class FixtureSyncPresenter(FixtureLibraryQuery library) : ISyncPresenter
{
    public SyncStatusView Status => library.Scenario switch
    {
        // A library nobody has synced yet. Nothing is wrong with it, which is
        // exactly what makes it a different state from the three below.
        Scenario.Empty => SyncStatusView.Idle,

        Scenario.InvalidKey => SyncStatusView.Idle with { Problem = SyncProblem.InvalidKey },

        Scenario.PrivateProfile => SyncStatusView.Idle with { Problem = SyncProblem.PrivateProfile },

        Scenario.OtherAccount => SyncStatusView.Idle with { Problem = SyncProblem.OtherAccount },

        Scenario.CircuitOpen => new SyncStatusView(
            SyncPhase.CircuitOpen, 412, 1482, "Divinity: Original Sin 2 — schema",
            "~6 min left", "4.8 req/s",
            "Paused after five consecutive failures",
            "Steam returned 429 five times in a row. Waiting 8 s before the next attempt."),

        _ => new SyncStatusView(
            SyncPhase.Running, 412, 1482, "Divinity: Original Sin 2 — schema",
            "~6 min left", "4.8 req/s", null, null),
    };
}
