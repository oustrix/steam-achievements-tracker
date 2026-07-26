using Microsoft.Data.Sqlite;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Data;

/// <summary>
/// Owns a writable connection of its own. WAL allows readers alongside a
/// writer but still permits only one writer at a time, so a click on the
/// accent picker during a sync would otherwise fail with SQLITE_BUSY. The
/// connection this is constructed with is expected to carry a busy timeout;
/// a single-row update against <c>settings</c> finishes in microseconds, so
/// waiting out a sync transaction is invisible.
/// </summary>
public sealed class SqliteUserPreferences : IUserPreferences
{
    private readonly SqliteConnection _connection;

    public SqliteUserPreferences(SqliteConnection connection) => _connection = connection;

    public string? Accent => Settings.ReadText(_connection, Column);

    public void SetAccent(string accent) => Settings.Write(_connection, Column, accent);

    private const string Column = "accent";
}
