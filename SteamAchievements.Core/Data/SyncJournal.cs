using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

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
    /// history says so.
    /// </summary>
    public const string Cancelled = "cancelled";

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
    public void MarkSyncCompleted(DateTimeOffset at) => _connection.Execute("""
        INSERT INTO settings (id, last_full_sync_at) VALUES (1, @At)
        ON CONFLICT(id) DO UPDATE SET last_full_sync_at = excluded.last_full_sync_at;
        """, new { At = at.ToString("o") });

    public DateTimeOffset? LastSyncedAt
    {
        get
        {
            var stored = _connection.QuerySingleOrDefault<string?>(
                "SELECT last_full_sync_at FROM settings WHERE id = 1");

            return stored is null ? null : DateTimeOffset.Parse(stored, CultureInfo.InvariantCulture);
        }
    }
}
