using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SteamAchievements.Core.App;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Sync;

namespace SteamAchievements.Core.Tests.App;

public class SyncCoordinatorTests
{
    private static readonly ulong SteamId = 76561190000000002;
    private static readonly DateTimeOffset Start = new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Advances a second per read, so elapsed time is non-zero and ordered
    /// without any test having to wait. How many times a run reads it is an
    /// implementation detail — it changes when progress reporting does — so
    /// assertions below check ordering rather than counting seconds.
    /// </summary>
    private sealed class SteppingClock
    {
        private DateTimeOffset _now = Start;

        public DateTimeOffset Read()
        {
            var current = _now;
            _now = _now.AddSeconds(1);
            return current;
        }
    }

    private sealed class FakeSyncRunner : ISyncRunner
    {
        private readonly Func<IProgress<SyncProgress>?, CancellationToken, Task> _behaviour;

        public FakeSyncRunner(Func<IProgress<SyncProgress>?, CancellationToken, Task> behaviour) =>
            _behaviour = behaviour;

        public bool LastForce { get; private set; }

        public ulong LastSteamId { get; private set; }

        public Task RunAsync(
            ulong steamId, bool force, IProgress<SyncProgress>? progress, CancellationToken cancellationToken)
        {
            LastSteamId = steamId;
            LastForce = force;
            return _behaviour(progress, cancellationToken);
        }
    }

    /// <summary>A task that only ends when the caller cancels.</summary>
    private static Task UntilCancelled(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource();
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return completion.Task;
    }

    private static (SyncCoordinator Coordinator, Microsoft.Data.Sqlite.SqliteConnection Connection, IAccountStore Accounts)
        Build(ISyncRunner runner, bool withAccount = true, ILogger<SyncCoordinator>? log = null)
    {
        var connection = Database.Open(":memory:");
        var accounts = new SqliteAccountStore(connection);

        if (withAccount)
        {
            accounts.Set(SteamId, "oustrix", "avatar");
        }

        var clock = new SteppingClock();
        return (
            new SyncCoordinator(
                runner, accounts, new SyncJournal(connection), clock.Read,
                log ?? NullLogger<SyncCoordinator>.Instance),
            connection, accounts);
    }

    [Fact]
    public void StartsIdleAndUnblocked()
    {
        var (coordinator, connection, _) = Build(new FakeSyncRunner((_, _) => Task.CompletedTask));
        using (connection)
        using (coordinator)
        {
            Assert.Equal(SyncPhase.Idle, coordinator.Status.Phase);
            Assert.Equal(SyncProblem.None, coordinator.Status.Problem);
        }
    }

    [Fact]
    public void DoesNotTreatAPreviousSuccessfulSyncAsAProblem()
    {
        var connection = Database.Open(":memory:");
        var accounts = new SqliteAccountStore(connection);
        accounts.Set(SteamId, "oustrix", "avatar");
        var journal = new SyncJournal(connection);
        journal.MarkSyncCompleted(Start);

        var clock = new SteppingClock();
        using (connection)
        using (var coordinator = new SyncCoordinator(
                   new FakeSyncRunner((_, _) => Task.CompletedTask), accounts, journal, clock.Read,
                   NullLogger<SyncCoordinator>.Instance))
        {
            Assert.Equal(SyncPhase.Idle, coordinator.Status.Phase);
            Assert.Equal(SyncProblem.None, coordinator.Status.Problem);
        }
    }

    [Fact]
    public void ReportsARejectedKeyAtStartupWhenTheFlagIsSet()
    {
        var (coordinator, connection, accounts) = Build(new FakeSyncRunner((_, _) => Task.CompletedTask));
        using (connection)
        {
            coordinator.Dispose();
            accounts.MarkKeyRejected(Start);

            var clock = new SteppingClock();
            using var fresh = new SyncCoordinator(
                new FakeSyncRunner((_, _) => Task.CompletedTask), accounts, new SyncJournal(connection), clock.Read,
                NullLogger<SyncCoordinator>.Instance);

            Assert.Equal(SyncProblem.InvalidKey, fresh.Status.Problem);
        }
    }

    [Fact]
    public async Task PublishesProgressWhileRunningAndSettlesOnIdle()
    {
        var seen = new List<SyncStatusView>();
        var runner = new FakeSyncRunner((progress, _) =>
        {
            progress!.Report(new SyncProgress(1, 2, "Half-Life 2"));
            progress.Report(new SyncProgress(2, 2, "Portal"));
            return Task.CompletedTask;
        });

        var (coordinator, connection, _) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Changed += () => seen.Add(coordinator.Status);
            coordinator.Start(force: false);
            await coordinator.Completion;

            Assert.Contains(seen, s => s is { Phase: SyncPhase.Running, Completed: 1, CurrentGame: "Half-Life 2" });
            Assert.Equal(SyncPhase.Idle, coordinator.Status.Phase);
        }
    }

    [Fact]
    public async Task RecordsASuccessfulRunAndItsCompletionTime()
    {
        var runner = new FakeSyncRunner((progress, _) =>
        {
            progress!.Report(new SyncProgress(7, 7, "Portal"));
            return Task.CompletedTask;
        });

        var (coordinator, connection, _) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: true);
            await coordinator.Completion;

            var run = new SqliteLibraryQuery(connection).GetSyncHistory(10, Start.AddHours(1)).Single();
            Assert.Equal(SyncRunOutcome.Completed, run.Outcome);
            Assert.Contains("Full sync", run.WhatText);

            // The rule is "a successful run records when it finished", not any
            // particular number of clock ticks.
            Assert.True(new SyncJournal(connection).LastSyncedAt > Start);
        }
    }

    [Fact]
    public async Task PassesForceThroughToTheRunner()
    {
        var runner = new FakeSyncRunner((_, _) => Task.CompletedTask);
        var (coordinator, connection, _) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: true);
            await coordinator.Completion;

            Assert.True(runner.LastForce);
            Assert.Equal(SteamId, runner.LastSteamId);
        }
    }

    [Fact]
    public async Task PausingLeavesTheProgressVisibleAndRecordsACancelledRun()
    {
        var runner = new FakeSyncRunner((progress, token) =>
        {
            progress!.Report(new SyncProgress(412, 1482, "Stellaris"));
            return UntilCancelled(token);
        });

        var (coordinator, connection, _) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            coordinator.Pause();
            await coordinator.Completion;

            Assert.Equal(SyncPhase.Paused, coordinator.Status.Phase);
            Assert.Equal(412, coordinator.Status.Completed);
            Assert.Equal(1482, coordinator.Status.Total);

            var run = new SqliteLibraryQuery(connection).GetSyncHistory(10, Start.AddHours(1)).Single();
            Assert.Equal(SyncRunOutcome.Cancelled, run.Outcome);
        }
    }

    [Fact]
    public async Task CancellingDiffersFromPausingOnlyInThePhaseItLeavesBehind()
    {
        var runner = new FakeSyncRunner((progress, token) =>
        {
            progress!.Report(new SyncProgress(5, 100, "Stellaris"));
            return UntilCancelled(token);
        });

        var (coordinator, connection, _) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            coordinator.Cancel();
            await coordinator.Completion;

            Assert.Equal(SyncPhase.Idle, coordinator.Status.Phase);
            Assert.Equal(
                SyncRunOutcome.Cancelled,
                new SqliteLibraryQuery(connection).GetSyncHistory(10, Start.AddHours(1)).Single().Outcome);
        }
    }

    [Fact]
    public async Task ResumingAfterAPauseIsJustStartingAgain()
    {
        var runs = 0;
        var runner = new FakeSyncRunner((progress, token) =>
        {
            runs++;
            progress!.Report(new SyncProgress(10, 100, "Stellaris"));
            return runs == 1 ? UntilCancelled(token) : Task.CompletedTask;
        });

        var (coordinator, connection, _) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            coordinator.Pause();
            await coordinator.Completion;

            coordinator.Start(force: false);
            await coordinator.Completion;

            Assert.Equal(SyncPhase.Idle, coordinator.Status.Phase);
            Assert.Equal(2, new SqliteLibraryQuery(connection).GetSyncHistory(10, Start.AddHours(1)).Count);
        }
    }

    [Fact]
    public async Task AKeyRevokedMidRunFlagsTheKeyAndKeepsTheProgressOnScreen()
    {
        var runner = new FakeSyncRunner((progress, _) =>
        {
            progress!.Report(new SyncProgress(400, 1482, "Stellaris"));
            throw new SteamApiException(SteamApiErrorKind.InvalidKey, 401, "Steam rejected the API key. Check it in settings.");
        });

        var (coordinator, connection, accounts) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            await coordinator.Completion;

            Assert.Equal(SyncProblem.InvalidKey, coordinator.Status.Problem);
            Assert.Equal(400, coordinator.Status.Completed);
            Assert.NotNull(accounts.KeyRejectedAt);
            Assert.Equal(
                SyncRunOutcome.Failed,
                new SqliteLibraryQuery(connection).GetSyncHistory(10, Start.AddHours(1)).Single().Outcome);
        }
    }

    [Fact]
    public async Task ASuccessfulRunClearsAPreviousKeyRejection()
    {
        var (coordinator, connection, accounts) = Build(new FakeSyncRunner((_, _) => Task.CompletedTask));
        using (connection)
        using (coordinator)
        {
            accounts.MarkKeyRejected(Start);

            coordinator.Start(force: false);
            await coordinator.Completion;

            Assert.Null(accounts.KeyRejectedAt);
        }
    }

    [Fact]
    public async Task ANetworkFailureEndsInFailedRatherThanKeyRejected()
    {
        var runner = new FakeSyncRunner((_, _) =>
            throw new SteamApiException(SteamApiErrorKind.ServerError, 503, "Steam returned 503."));

        var (coordinator, connection, accounts) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            await coordinator.Completion;

            Assert.Equal(SyncProblem.None, coordinator.Status.Problem);
            Assert.Equal("Sync failed", coordinator.Status.AlertTitle);
            Assert.Equal("Steam returned 503.", coordinator.Status.AlertBody);
            Assert.Null(accounts.KeyRejectedAt);
        }
    }

    [Fact]
    public async Task RefusesToStartASecondRunWhileOneIsInFlight()
    {
        var starts = 0;
        var runner = new FakeSyncRunner((_, token) =>
        {
            starts++;
            return UntilCancelled(token);
        });

        var (coordinator, connection, _) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            coordinator.Start(force: false);
            coordinator.Cancel();
            await coordinator.Completion;

            Assert.Equal(1, starts);
        }
    }

    [Fact]
    public async Task FailsImmediatelyWhenNoAccountIsConfigured()
    {
        var (coordinator, connection, _) = Build(new FakeSyncRunner((_, _) => Task.CompletedTask), withAccount: false);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            await coordinator.Completion;

            Assert.Equal("No Steam account is configured", coordinator.Status.AlertTitle);
            Assert.Empty(new SqliteLibraryQuery(connection).GetSyncHistory(10, Start.AddHours(1)));
        }
    }

    [Fact]
    public void LogsTheStartAndTheCompletionOfASuccessfulRun()
    {
        var log = new RecordingLogger<SyncCoordinator>();
        var (coordinator, connection, _) = Build(
            new FakeSyncRunner((_, _) => Task.CompletedTask), log: log);

        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            coordinator.Completion.Wait();
        }

        Assert.True(log.Logged("sync started"));
        Assert.True(log.Logged("sync completed"));
    }

    [Fact]
    public void LogsAFailedRunWithItsException()
    {
        var log = new RecordingLogger<SyncCoordinator>();
        var (coordinator, connection, _) = Build(
            new FakeSyncRunner((_, _) => Task.FromException(new InvalidOperationException("boom"))),
            log: log);

        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            coordinator.Completion.Wait();
        }

        // Error, not merely present: a failed sync is the design's stated
        // "real failure" promise, and a downgrade to Warning or Information
        // would still leave every other assertion here green.
        Assert.True(log.LoggedAt(LogLevel.Error, "sync failed"));
        Assert.Contains(log.Errors, e => e?.Message == "boom");
    }

    [Fact]
    public void LogsAPauseAsAPauseRatherThanACancellation()
    {
        var log = new RecordingLogger<SyncCoordinator>();
        var (coordinator, connection, _) = Build(
            new FakeSyncRunner((_, token) => UntilCancelled(token)), log: log);

        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            coordinator.Pause();
            coordinator.Completion.Wait(TimeSpan.FromSeconds(5));
        }

        Assert.True(log.Logged("sync pause requested"));
        Assert.True(log.Logged("sync paused"));
    }

    // Scoped to the happy-path run rather than named after a class-wide
    // guarantee: the constructor's key-rejection warning legitimately
    // contains the word "key" (it names the condition, never the secret
    // value — SyncCoordinator never reads the value at all), so a
    // whole-class claim would be false the moment that branch runs.
    [Fact]
    public void AStartAndCompleteRunNeverMentionsAKey()
    {
        var log = new RecordingLogger<SyncCoordinator>();
        var (coordinator, connection, _) = Build(
            new FakeSyncRunner((_, _) => Task.CompletedTask), log: log);

        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            coordinator.Completion.Wait();
        }

        Assert.DoesNotContain(log.Lines, line => line.Contains("key", StringComparison.OrdinalIgnoreCase));
    }
}
