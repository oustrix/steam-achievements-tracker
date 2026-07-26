using Dapper;
using Microsoft.Data.Sqlite;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Data;

/// <summary>
/// Owns a writable connection of its own. WAL allows readers alongside a
/// writer but still permits only one writer at a time, so a click on the
/// accent picker during a sync would otherwise fail with SQLITE_BUSY.
/// <see cref="Database.Open"/> sets the busy timeout that turns that failure
/// into a short wait, so a connection from there is all this needs.
/// </summary>
public sealed class SqliteUserPreferences : IUserPreferences
{
    private readonly SqliteConnection _connection;
    private string? _accent;
    private bool _loaded;

    public SqliteUserPreferences(SqliteConnection connection) => _connection = connection;

    /// <summary>
    /// Read from the database once and served from memory afterwards. The
    /// shell renders the accent into a CSS variable on every render and the
    /// settings screen reads it again for the picker, so a single click would
    /// otherwise be three synchronous round-trips on the UI thread. Nothing
    /// outside this type writes <c>settings.accent</c>, so the cached value
    /// cannot go stale behind its back; keeping the memoization here rather
    /// than in a decorator means the invariant does not depend on whoever
    /// registers this service later.
    /// </summary>
    public string? Accent
    {
        get
        {
            if (!_loaded)
            {
                _accent = _connection.QuerySingleOrDefault<string?>(
                    "SELECT accent FROM settings WHERE id = 1");
                _loaded = true;
            }

            return _accent;
        }
    }

    public event Action? Changed;

    public void SetAccent(string accent)
    {
        _connection.Execute("""
            INSERT INTO settings (id, accent) VALUES (1, @Accent)
            ON CONFLICT(id) DO UPDATE SET accent = excluded.accent;
            """, new { Accent = accent });

        // Before Changed, not after: a subscriber reads Accent from inside the
        // handler to repaint, and it has to see the colour that was just
        // written.
        _accent = accent;
        _loaded = true;

        Changed?.Invoke();
    }
}
