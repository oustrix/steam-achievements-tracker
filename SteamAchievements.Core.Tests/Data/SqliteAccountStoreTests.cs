using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Tests.Data;

public class SqliteAccountStoreTests
{
    private static readonly ulong SteamId = 76561190000000002;

    [Fact]
    public void ReportsNoAccountBeforeOnboarding()
    {
        using var connection = Database.Open(":memory:");

        Assert.Null(new SqliteAccountStore(connection).Current);
    }

    [Fact]
    public void RoundTripsTheStoredAccount()
    {
        using var connection = Database.Open(":memory:");
        var store = new SqliteAccountStore(connection);

        store.Set(SteamId, "oustrix", "https://example.invalid/avatar_full.jpg");

        var current = store.Current;
        Assert.NotNull(current);
        Assert.Equal(SteamId, current.SteamId64);
        Assert.Equal("oustrix", current.PersonaName);
        Assert.Equal("https://example.invalid/avatar_full.jpg", current.AvatarUrl);
    }

    [Fact]
    public void ReplacesTheAccountInsteadOfAddingARow()
    {
        using var connection = Database.Open(":memory:");
        var store = new SqliteAccountStore(connection);

        store.Set(SteamId, "oustrix", "a");
        store.Set(76561190000000003, "someone-else", "b");

        Assert.Equal(76561190000000003UL, store.Current!.SteamId64);
        Assert.Equal(1, Dapper.SqlMapper.QuerySingle<long>(connection, "SELECT COUNT(*) FROM settings"));
    }

    [Fact]
    public void PreservesTheAccentWhenWritingTheAccount()
    {
        using var connection = Database.Open(":memory:");
        new SqliteUserPreferences(connection).SetAccent("#c98f7a");

        new SqliteAccountStore(connection).Set(SteamId, "oustrix", "a");

        Assert.Equal("#c98f7a", new SqliteUserPreferences(connection).Accent);
    }

    [Fact]
    public void ReportsNoRejectionBeforeOneHappened()
    {
        using var connection = Database.Open(":memory:");

        Assert.Null(new SqliteAccountStore(connection).KeyRejectedAt);
    }

    [Fact]
    public void RoundTripsTheRejectionTimestamp()
    {
        using var connection = Database.Open(":memory:");
        var store = new SqliteAccountStore(connection);
        var at = new DateTimeOffset(2026, 7, 26, 12, 30, 0, TimeSpan.Zero);

        store.MarkKeyRejected(at);

        Assert.Equal(at, store.KeyRejectedAt);
    }

    [Fact]
    public void ClearsTheRejectionWithoutDisturbingTheAccount()
    {
        using var connection = Database.Open(":memory:");
        var store = new SqliteAccountStore(connection);
        store.Set(SteamId, "oustrix", "a");
        store.MarkKeyRejected(DateTimeOffset.UtcNow);

        store.ClearKeyRejected();

        Assert.Null(store.KeyRejectedAt);
        Assert.Equal(SteamId, store.Current!.SteamId64);
    }

    [Fact]
    public void MarksRejectionEvenWhenNoSettingsRowExistsYet()
    {
        using var connection = Database.Open(":memory:");

        new SqliteAccountStore(connection).MarkKeyRejected(DateTimeOffset.UtcNow);

        Assert.NotNull(new SqliteAccountStore(connection).KeyRejectedAt);
    }
}
