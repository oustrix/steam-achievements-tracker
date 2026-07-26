using SteamAchievements.UI.State;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>Shows the sync screen under each state without running a sync.</summary>
public sealed class FixtureSyncPresenter(FixtureLibraryQuery library) : ISyncPresenter
{
    public SyncStatusView Status => library.Scenario switch
    {
        Scenario.Empty or Scenario.InvalidKey or Scenario.PrivateProfile => SyncStatusView.Idle,

        Scenario.OtherAccount => new SyncStatusView(
            SyncPhase.CircuitOpen, 412, 1482, "Divinity: Original Sin 2 — schema",
            "~6 min left", "4.8 req/s",
            "Paused after five consecutive failures",
            "Steam returned 429 five times in a row. Waiting 8 s before the next attempt."),

        _ => new SyncStatusView(
            SyncPhase.Running, 412, 1482, "Divinity: Original Sin 2 — schema",
            "~6 min left", "4.8 req/s", null, null),
    };
}
