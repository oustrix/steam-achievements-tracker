using Dapper;
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

    public string? Accent =>
        _connection.QuerySingleOrDefault<string?>("SELECT accent FROM settings WHERE id = 1");

    public void SetAccent(string accent) => _connection.Execute("""
        INSERT INTO settings (id, accent) VALUES (1, @Accent)
        ON CONFLICT(id) DO UPDATE SET accent = excluded.accent;
        """, new { Accent = accent });
}
