using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests.Presentation;

public class LibraryChangeSignalTests
{
    private static SyncStatusView Running(int completed) =>
        SyncStatusView.Idle with { Phase = SyncPhase.Running, Completed = completed, Total = 100 };

    [Fact]
    public void RaisesOnceWhenTheSyncStops()
    {
        var sync = new FakeSyncPresenter();
        var accounts = new FakeAccountAdmin();
        using var signal = new LibraryChangeSignal(sync, accounts);

        var raised = 0;
        signal.Changed += () => raised++;

        sync.Publish(Running(1));
        sync.Publish(SyncStatusView.Idle);

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// Progress arrives about five times a second and GetQueue re-ranks the
    /// whole library. Re-reading on each report would run a full ranking pass
    /// under a writing sync and reorder rows beneath the user's cursor.
    /// </summary>
    [Fact]
    public void StaysQuietWhileTheSyncRuns()
    {
        var sync = new FakeSyncPresenter();
        using var signal = new LibraryChangeSignal(sync, new FakeAccountAdmin());

        var raised = 0;
        signal.Changed += () => raised++;

        sync.Publish(Running(1));
        sync.Publish(Running(2));
        sync.Publish(Running(3));

        Assert.Equal(0, raised);
    }

    [Fact]
    public void StaysQuietWhenTheSyncStarts()
    {
        var sync = new FakeSyncPresenter();
        using var signal = new LibraryChangeSignal(sync, new FakeAccountAdmin());

        var raised = 0;
        signal.Changed += () => raised++;

        sync.Publish(Running(1));

        Assert.Equal(0, raised);
    }

    /// <summary>
    /// A switch and a reset both empty the library, and neither goes through a
    /// sync phase at all.
    /// </summary>
    [Fact]
    public void RaisesOnEveryAccountChange()
    {
        var accounts = new FakeAccountAdmin();
        using var signal = new LibraryChangeSignal(new FakeSyncPresenter(), accounts);

        var raised = 0;
        signal.Changed += () => raised++;

        accounts.Raise();
        accounts.ResetEverything();

        Assert.Equal(2, raised);
    }

    [Fact]
    public void StopsListeningAfterDispose()
    {
        var sync = new FakeSyncPresenter();
        var accounts = new FakeAccountAdmin();
        var signal = new LibraryChangeSignal(sync, accounts);

        var raised = 0;
        signal.Changed += () => raised++;

        signal.Dispose();

        sync.Publish(Running(1));
        sync.Publish(SyncStatusView.Idle);
        accounts.Raise();

        Assert.Equal(0, raised);
    }

    /// <summary>
    /// The signal is built while a sync is already in flight — the host
    /// resolves it at startup, and a sync can be running by then only in the
    /// sense that a later construction is legal. Starting from "running" means
    /// the first stop is still an edge.
    /// </summary>
    [Fact]
    public void TakesItsStartingStateFromThePresenter()
    {
        var sync = new FakeSyncPresenter();
        sync.Publish(Running(1));

        using var signal = new LibraryChangeSignal(sync, new FakeAccountAdmin());

        var raised = 0;
        signal.Changed += () => raised++;

        sync.Publish(SyncStatusView.Idle);

        Assert.Equal(1, raised);
    }
}
