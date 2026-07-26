using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace SteamAchievements.Core.Data;

/// <summary>
/// Expects the settings connection — the third one, the one carrying a
/// busy timeout. WAL permits a single writer, so a settings write during a sync
/// waits rather than failing with SQLITE_BUSY.
/// </summary>
public sealed class SqliteAccountStore : IAccountStore
{
    private readonly SqliteConnection _connection;

    public SqliteAccountStore(SqliteConnection connection) => _connection = connection;

    // Dapper needs an exact CLR type match and no snake_case translation, so
    // every column is aliased and every value arrives as the type the schema
    // declares. steam_id64 is TEXT in the schema, so it is read as a string and
    // parsed here.
    private sealed record AccountRow(string? SteamId64, string? PersonaName, string? AvatarUrl);

    public StoredAccount? Current
    {
        get
        {
            var row = _connection.QuerySingleOrDefault<AccountRow>("""
                SELECT steam_id64   AS SteamId64,
                       persona_name AS PersonaName,
                       avatar_url   AS AvatarUrl
                FROM settings WHERE id = 1
                """);

            if (row?.SteamId64 is null || !ulong.TryParse(row.SteamId64, out var steamId) || steamId == 0)
            {
                return null;
            }

            return new StoredAccount(steamId, row.PersonaName ?? string.Empty, row.AvatarUrl ?? string.Empty);
        }
    }

    public void Set(ulong steamId64, string personaName, string avatarUrl) => _connection.Execute("""
        INSERT INTO settings (id, steam_id64, persona_name, avatar_url)
        VALUES (1, @SteamId, @Persona, @Avatar)
        ON CONFLICT(id) DO UPDATE SET
            steam_id64   = excluded.steam_id64,
            persona_name = excluded.persona_name,
            avatar_url   = excluded.avatar_url;
        """, new
    {
        SteamId = steamId64.ToString(CultureInfo.InvariantCulture),
        Persona = personaName,
        Avatar = avatarUrl,
    });

    public DateTimeOffset? KeyRejectedAt
    {
        get
        {
            var stored = _connection.QuerySingleOrDefault<string?>(
                "SELECT key_rejected_at FROM settings WHERE id = 1");

            return stored is null ? null : DateTimeOffset.Parse(stored, CultureInfo.InvariantCulture);
        }
    }

    public void MarkKeyRejected(DateTimeOffset at) => WriteRejection(at.ToString("o"));

    public void ClearKeyRejected() => WriteRejection(null);

    private void WriteRejection(string? value) => _connection.Execute("""
        INSERT INTO settings (id, key_rejected_at) VALUES (1, @Value)
        ON CONFLICT(id) DO UPDATE SET key_rejected_at = excluded.key_rejected_at;
        """, new { Value = value });
}
