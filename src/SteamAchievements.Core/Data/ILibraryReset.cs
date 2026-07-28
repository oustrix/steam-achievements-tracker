using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SteamAchievements.Core.Diagnostics;

namespace SteamAchievements.Core.Data;

/// <summary>
/// Emptying the library, behind a seam like every other data touchpoint in
/// Core.
///
/// Exists so that a caller cannot be handed the wrong connection. Reaching
/// straight for the static <see cref="Database.ResetLibrary"/> means passing a
/// <c>SqliteConnection</c> by hand, and the read-only connection compiles just
/// as well as the writable one — failing at runtime, on Windows, on the one
/// path that destroys data.
/// </summary>
public interface ILibraryReset
{
    void Reset();
}

public sealed class SqliteLibraryReset : ILibraryReset
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<SqliteLibraryReset> _log;

    /// <param name="connection">Must be writable and carry a busy timeout — see <see cref="Database.OpenSettings"/>.</param>
    public SqliteLibraryReset(SqliteConnection connection, ILogger<SqliteLibraryReset> log)
    {
        _connection = connection;
        _log = log;
    }

    /// <summary>
    /// Empties the library, then reclaims the space — timed separately, not as
    /// one number. "The reset took forty seconds" and "the VACUUM took
    /// thirty-nine of them" are different findings, and this is the one method
    /// with a logger of its own to tell them apart with.
    /// </summary>
    public void Reset()
    {
        var started = Stopwatch.GetTimestamp();
        Database.EmptyLibrary(_connection);
        _log.LogInformation("library emptied in {Elapsed}ms", Elapsed.Since(started));

        // VACUUM cannot run inside a transaction, so it is deliberately
        // separate from EmptyLibrary above and timed on its own — this is the
        // one statement whose behaviour with three live connections against a
        // WAL database has never been observed.
        var vacuumStarted = Stopwatch.GetTimestamp();
        Database.Vacuum(_connection);
        _log.LogInformation("vacuum finished in {Elapsed}ms", Elapsed.Since(vacuumStarted));
    }
}
