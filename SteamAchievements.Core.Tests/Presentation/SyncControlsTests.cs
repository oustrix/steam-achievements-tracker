using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests.Presentation;

public class SyncControlsTests
{
    private static SyncStatusView Idle(SyncProblem problem = SyncProblem.None) =>
        SyncStatusView.Idle with { Problem = problem };

    [Fact]
    public void IdleOffersASync()
    {
        var controls = SyncControls.For(Idle());

        Assert.Equal("Sync", controls.PrimaryLabel);
        Assert.True(controls.PrimaryEnabled);
        Assert.False(controls.ShowCancel);
    }

    /// <summary>
    /// Retrying cannot help until the key is replaced, and the screen's own
    /// notice links to settings. An enabled button here spends requests to
    /// rediscover what is already known.
    /// </summary>
    [Fact]
    public void ARejectedKeyDisablesTheButton()
    {
        Assert.False(SyncControls.For(Idle(SyncProblem.InvalidKey)).PrimaryEnabled);
    }

    /// <summary>The user may have just made their game details public.</summary>
    [Fact]
    public void APrivateProfileStillOffersARetry()
    {
        Assert.True(SyncControls.For(Idle(SyncProblem.PrivateProfile)).PrimaryEnabled);
    }

    [Fact]
    public void ADifferentAccountDisablesTheButton()
    {
        Assert.False(SyncControls.For(Idle(SyncProblem.OtherAccount)).PrimaryEnabled);
    }

    [Fact]
    public void RunningOffersPauseAndCancel()
    {
        var controls = SyncControls.For(SyncStatusView.Idle with { Phase = SyncPhase.Running });

        Assert.Equal("Pause", controls.PrimaryLabel);
        Assert.True(controls.PrimaryEnabled);
        Assert.True(controls.ShowCancel);
    }

    [Fact]
    public void PausedOffersResume()
    {
        var controls = SyncControls.For(SyncStatusView.Idle with { Phase = SyncPhase.Paused });

        Assert.Equal("Resume", controls.PrimaryLabel);
        Assert.True(controls.PrimaryEnabled);
        Assert.False(controls.ShowCancel);
    }

    [Fact]
    public void AnOpenCircuitDisablesTheButton()
    {
        var controls = SyncControls.For(SyncStatusView.Idle with { Phase = SyncPhase.CircuitOpen });

        Assert.False(controls.PrimaryEnabled);
        Assert.Contains("retry", controls.Title);
    }

    /// <summary>
    /// SyncCoordinator publishes no CircuitOpen today, so most of this grid is
    /// unreachable. The mapping is still total, so that whoever makes one of
    /// these states reachable gets a button rather than an exception thrown
    /// inside a render, on Windows, where it costs a full CI cycle to see.
    /// </summary>
    [Fact]
    public void EveryPhaseAndProblemPairIsMapped()
    {
        foreach (var phase in Enum.GetValues<SyncPhase>())
        {
            foreach (var problem in Enum.GetValues<SyncProblem>())
            {
                var controls = SyncControls.For(
                    SyncStatusView.Idle with { Phase = phase, Problem = problem });

                Assert.NotEqual("", controls.Title);
                Assert.NotEqual("", controls.PrimaryLabel);
            }
        }
    }
}
