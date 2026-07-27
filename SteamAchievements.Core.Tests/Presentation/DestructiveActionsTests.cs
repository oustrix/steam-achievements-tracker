using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests.Presentation;

public class DestructiveActionsTests
{
    private static SyncStatusView In(SyncPhase phase) => SyncStatusView.Idle with { Phase = phase };

    /// <summary>
    /// The one state with a live writer. SwitchToAsync and ResetEverything empty
    /// the tables while SyncOrchestrator is still writing the old account's
    /// games, and nothing in the schema records which account a row came from —
    /// so the result is a silently blended library rather than an error.
    /// </summary>
    [Fact]
    public void BlocksWhileTheSyncIsRunning()
    {
        var gate = DestructiveActions.For(In(SyncPhase.Running));

        Assert.False(gate.Allowed);
        Assert.NotNull(gate.BlockedReason);
    }

    /// <summary>The reason is the copy the screen shows, so it has to say what to do about it.</summary>
    [Fact]
    public void SaysHowToUnblockItself()
    {
        var reason = DestructiveActions.For(In(SyncPhase.Running)).BlockedReason;

        Assert.NotNull(reason);
        Assert.Contains("sync", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pause", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllowsWhenIdle()
    {
        Assert.True(DestructiveActions.For(In(SyncPhase.Idle)).Allowed);
    }

    /// <summary>
    /// SyncCoordinator.Pause cancels the run, and the Paused status is published
    /// only after the run task has unwound — there is no writer left to race.
    /// </summary>
    [Fact]
    public void AllowsWhenPaused()
    {
        Assert.True(DestructiveActions.For(In(SyncPhase.Paused)).Allowed);
    }

    [Fact]
    public void AllowsWhenTheCircuitIsOpen()
    {
        Assert.True(DestructiveActions.For(In(SyncPhase.CircuitOpen)).Allowed);
    }

    /// <summary>
    /// A blocked gate whose reason is null renders an empty explanation, and an
    /// allowed gate carrying one renders a warning next to a working button.
    /// Both are silent on Windows, so the pairing is asserted across the grid.
    /// </summary>
    [Fact]
    public void EveryPhaseAndProblemPairAgreesWithItsReason()
    {
        foreach (var phase in Enum.GetValues<SyncPhase>())
        {
            foreach (var problem in Enum.GetValues<SyncProblem>())
            {
                var gate = DestructiveActions.For(
                    SyncStatusView.Idle with { Phase = phase, Problem = problem });

                if (gate.Allowed)
                {
                    Assert.Null(gate.BlockedReason);
                }
                else
                {
                    Assert.False(string.IsNullOrWhiteSpace(gate.BlockedReason));
                }
            }
        }
    }
}
