using Microsoft.Data.Sqlite;

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

    /// <param name="connection">Must be writable and carry a busy timeout — see <see cref="Database.OpenSettings"/>.</param>
    public SqliteLibraryReset(SqliteConnection connection) => _connection = connection;

    public void Reset() => Database.ResetLibrary(_connection);
}
