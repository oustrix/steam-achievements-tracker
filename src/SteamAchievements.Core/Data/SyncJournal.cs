using Dapper;
using Microsoft.Data.Sqlite;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Data;

/// <summary>One completed attempt at syncing, however it ended.</summary>
public sealed record SyncRunRecord(
    DateTimeOffset StartedAt,
    string Kind,
    int GamesSynced,
    long DurationMs,
    string? Error);

/// <summary>
/// Writes the two things the history screen and the sidebar read and nobody
/// used to write.
///
/// Expects the settings connection rather than the sync engine's. Every write
/// here happens after <c>RunAsync</c> has returned, so no sync transaction is in
/// flight; the busy timeout on that connection covers the rest.
/// </summary>
public sealed class SyncJournal
{
    /// <summary>
    /// Stored in <c>sync_runs.error</c> for a run the user stopped. Pausing is
    /// cancel-and-resume, so a paused sync leaves one of these behind and the
    /// history says so. The value itself is declared on
    /// <see cref="SyncRunView"/>, which is where the screen decodes it.
    /// </summary>
    public const string Cancelled = SyncRunView.CancelledMarker;

    private readonly SqliteConnection _connection;

    public SyncJournal(SqliteConnection connection) => _connection = connection;

    public void RecordRun(SyncRunRecord run) => _connection.Execute("""
        INSERT INTO sync_runs (started_at, kind, games_synced, duration_ms, error)
        VALUES (@StartedAt, @Kind, @GamesSynced, @DurationMs, @Error)
        ON CONFLICT(started_at) DO UPDATE SET
            kind         = excluded.kind,
            games_synced = excluded.games_synced,
            duration_ms  = excluded.duration_ms,
            error        = excluded.error;
        """, new
    {
        StartedAt = run.StartedAt.ToString("o"),
        run.Kind,
        run.GamesSynced,
        run.DurationMs,
        run.Error,
    });

    /// <summary>
    /// The column is named <c>last_full_sync_at</c> from the first migration but
    /// means "last successful sync": the sidebar renders it as "Last sync 14 min
    /// ago" regardless of whether the run was full or incremental.
    /// </summary>
    public void MarkSyncCompleted(DateTimeOffset at) =>
        Settings.WriteTimestamp(_connection, LastSyncColumn, at);

    public DateTimeOffset? LastSyncedAt => Settings.ReadTimestamp(_connection, LastSyncColumn);

    private const string LastSyncColumn = "last_full_sync_at";
}
