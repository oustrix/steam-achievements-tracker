using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace SteamAchievements.Core.Data;

/// <summary>
/// The <c>settings</c> table holds exactly one row, at <c>id = 1</c>, and every
/// writer has to say so: insert-with-conflict-update rather than update, or the
/// first write on a fresh database silently does nothing.
///
/// Three classes own different columns of that row — <see cref="SqliteUserPreferences"/>
/// the accent, <see cref="SqliteAccountStore"/> the identity,
/// <see cref="SyncJournal"/> the last sync — and the split is deliberate. Only
/// the singleton-row protocol is shared, so it lives here and nowhere else.
/// </summary>
internal static class Settings
{
    /// <param name="column">
    /// Interpolated into the statement rather than parameterized: SQLite does
    /// not accept a parameter in a column position. Every caller passes a
    /// literal from this assembly, never anything user-supplied.
    /// </param>
    internal static void Write(SqliteConnection connection, string column, object? value) =>
        connection.Execute(
            $"""
             INSERT INTO settings (id, {column}) VALUES (1, @Value)
             ON CONFLICT(id) DO UPDATE SET {column} = excluded.{column};
             """,
            new { Value = value });

    internal static string? ReadText(SqliteConnection connection, string column) =>
        connection.QuerySingleOrDefault<string?>($"SELECT {column} FROM settings WHERE id = 1");

    /// <summary>
    /// Timestamps are stored with <c>ToString("o")</c> throughout this
    /// assembly, which is culture-invariant by definition; parsing says so
    /// explicitly so the round trip is legible in one place.
    /// </summary>
    internal static DateTimeOffset? ReadTimestamp(SqliteConnection connection, string column)
    {
        var stored = ReadText(connection, column);

        return stored is null ? null : DateTimeOffset.Parse(stored, CultureInfo.InvariantCulture);
    }

    internal static void WriteTimestamp(SqliteConnection connection, string column, DateTimeOffset? value) =>
        Write(connection, column, value?.ToString("o"));
}
