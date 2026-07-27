using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests.Data;

public class SyncJournalTests
{
    private static readonly DateTimeOffset Started = new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReportsNoPreviousSyncOnAFreshDatabase()
    {
        using var connection = Database.Open(":memory:");

        Assert.Null(new SyncJournal(connection).LastSyncedAt);
    }

    [Fact]
    public void RoundTripsTheCompletionTimestamp()
    {
        using var connection = Database.Open(":memory:");
        var journal = new SyncJournal(connection);

        journal.MarkSyncCompleted(Started);

        Assert.Equal(Started, journal.LastSyncedAt);
    }

    [Fact]
    public void RecordsASuccessfulRunAsCompleted()
    {
        using var connection = Database.Open(":memory:");
        new SyncJournal(connection).RecordRun(new SyncRunRecord(Started, "full", 1482, 9000, null));

        var history = new SqliteLibraryQuery(connection).GetSyncHistory(10, Now);

        Assert.Equal(SyncRunOutcome.Completed, history.Single().Outcome);
        // Composed rather than written out: Formatting.Number separates
        // thousands with a thin space (U+2009), and a literal ASCII space here
        // would fail the comparison for a reason invisible in the diff. The
        // existing SqliteLibraryQueryTests does the same.
        Assert.Equal($"Full sync — {Formatting.Number(1482)} games", history.Single().WhatText);
    }

    [Fact]
    public void RecordsACancelledRunAsCancelledRatherThanFailed()
    {
        using var connection = Database.Open(":memory:");
        new SyncJournal(connection).RecordRun(
            new SyncRunRecord(Started, "incremental", 412, 3000, SyncJournal.Cancelled));

        var run = new SqliteLibraryQuery(connection).GetSyncHistory(10, Now).Single();

        Assert.Equal(SyncRunOutcome.Cancelled, run.Outcome);
        Assert.Equal("Cancelled — 412 games", run.WhatText);
    }

    [Fact]
    public void RecordsAFailedRunWithItsMessage()
    {
        using var connection = Database.Open(":memory:");
        new SyncJournal(connection).RecordRun(
            new SyncRunRecord(Started, "incremental", 3, 400, "Steam rejected the API key."));

        var run = new SqliteLibraryQuery(connection).GetSyncHistory(10, Now).Single();

        Assert.Equal(SyncRunOutcome.Failed, run.Outcome);
        Assert.Equal("Failed — Steam rejected the API key.", run.WhatText);
    }

    [Fact]
    public void KeepsEveryRunSoAPausedSyncLeavesATrail()
    {
        using var connection = Database.Open(":memory:");
        var journal = new SyncJournal(connection);

        journal.RecordRun(new SyncRunRecord(Started, "incremental", 100, 1000, SyncJournal.Cancelled));
        journal.RecordRun(new SyncRunRecord(Started.AddMinutes(5), "incremental", 300, 1000, SyncJournal.Cancelled));
        journal.RecordRun(new SyncRunRecord(Started.AddMinutes(9), "incremental", 482, 1000, null));

        Assert.Equal(3, new SqliteLibraryQuery(connection).GetSyncHistory(10, Now).Count);
    }

    [Fact]
    public void OverwritesRatherThanThrowingWhenTwoRunsShareAStartTimestamp()
    {
        using var connection = Database.Open(":memory:");
        var journal = new SyncJournal(connection);

        journal.RecordRun(new SyncRunRecord(Started, "full", 1, 10, null));
        journal.RecordRun(new SyncRunRecord(Started, "full", 2, 20, null));

        var run = new SqliteLibraryQuery(connection).GetSyncHistory(10, Now).Single();
        Assert.Equal("Full sync — 2 games", run.WhatText);
    }
}
