using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SteamAchievements.Core.App;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Tests.Steam;

namespace SteamAchievements.Core.Tests.App;

public class AccountAdminServiceTests
{
    // Deliberately not 76561190000000002: that is the account the committed
    // loginusers.vdf fixture marks MostRecent, and the mismatch tests below need
    // the stored account to differ from the active one.
    private static readonly ulong Stored = 76561190000000009;
    private static readonly ulong Other = 76561190000000003;
    private static readonly ulong ActiveInFixture = TempSteamRoot.ActiveSteamId;

    private static async Task<(AccountAdminService Admin, MemorySecretStore Secrets, IAccountStore Accounts, Microsoft.Data.Sqlite.SqliteConnection Connection)>
        BuildAsync(string? steamPath = null, ILogger<AccountAdminService>? log = null)
    {
        var body = await File.ReadAllTextAsync(TestPaths.Data("profile_public.xml"));
        var connection = Database.Open(":memory:");
        var accounts = new SqliteAccountStore(connection);
        accounts.Set(Stored, "oustrix", "avatar");

        var repository = new GameRepository(connection);
        repository.UpsertOwnedGames([new OwnedGame(220, "Half-Life 2", "hash", 600, 0, null)]);

        var secrets = new MemorySecretStore("0123456789ABCDEF0123456789ABCDEF");
        var community = new SteamCommunityClient(
            new HttpClient(FakeHttpMessageHandler.Returning(HttpStatusCode.OK, body, "text/xml"))
            {
                BaseAddress = new Uri("https://steamcommunity.com/"),
            });

        var admin = new AccountAdminService(
            new SqliteLibraryReset(connection, NullLogger<SqliteLibraryReset>.Instance),
            accounts, secrets,
            new SteamAccountLocator(new FixedSteamPath(steamPath)), community,
            log ?? NullLogger<AccountAdminService>.Instance);

        return (admin, secrets, accounts, connection);
    }

    [Fact]
    public async Task ReportsTheStoredAccount()
    {
        var (admin, _, _, connection) = await BuildAsync();
        using (connection)
        {
            Assert.Equal(Stored, admin.Current!.SteamId64);
        }
    }

    [Fact]
    public async Task ReportsNoMismatchWhenSteamIsNotInstalled()
    {
        var (admin, _, _, connection) = await BuildAsync();
        using (connection)
        {
            Assert.Null(admin.Mismatch);
        }
    }

    [Fact]
    public async Task ReportsAMismatchWhenSteamIsSignedInAsSomebodyElse()
    {
        using var steam = new TempSteamRoot();
        var (admin, _, _, connection) = await BuildAsync(steam.Path);
        using (connection)
        {
            Assert.NotNull(admin.Mismatch);
            Assert.Equal(ActiveInFixture, admin.Mismatch.ActiveSteamId64);
            Assert.Equal(TempSteamRoot.ActiveAccountName, admin.Mismatch.ActiveAccountName);
        }
    }

    [Fact]
    public async Task ReportsNoMismatchWhenSteamIsSignedInAsTheStoredAccount()
    {
        using var steam = new TempSteamRoot();
        var (admin, _, accounts, connection) = await BuildAsync(steam.Path);
        using (connection)
        {
            accounts.Set(ActiveInFixture, "oustrix", "avatar");

            Assert.Null(admin.Mismatch);
        }
    }

    [Fact]
    public async Task SwitchingEmptiesTheLibraryAndStoresTheNewAccount()
    {
        var (admin, _, accounts, connection) = await BuildAsync();
        using (connection)
        {
            await admin.SwitchToAsync(Other, CancellationToken.None);

            Assert.Equal(Other, accounts.Current!.SteamId64);
            Assert.Equal(0, Dapper.SqlMapper.QuerySingle<long>(connection, "SELECT COUNT(*) FROM owned_games"));
        }
    }

    [Fact]
    public async Task SwitchingKeepsTheKeyBecauseItIsNotBoundToAnAccount()
    {
        var (admin, secrets, _, connection) = await BuildAsync();
        using (connection)
        {
            await admin.SwitchToAsync(Other, CancellationToken.None);

            Assert.NotNull(secrets.Secret);
        }
    }

    [Fact]
    public async Task ResettingEverythingAlsoDiscardsTheKey()
    {
        var (admin, secrets, accounts, connection) = await BuildAsync();
        using (connection)
        {
            admin.ResetEverything();

            Assert.Null(secrets.Secret);
            Assert.Null(accounts.Current);
            Assert.Equal(0, Dapper.SqlMapper.QuerySingle<long>(connection, "SELECT COUNT(*) FROM owned_games"));
        }
    }

    [Fact]
    public async Task ResettingKeepsTheAccent()
    {
        var (admin, _, _, connection) = await BuildAsync();
        using (connection)
        {
            new SqliteUserPreferences(connection).SetAccent("#c98f7a");

            admin.ResetEverything();

            Assert.Equal("#c98f7a", new SqliteUserPreferences(connection).Accent);
        }
    }

    [Fact]
    public async Task LogsTheResetBecauseItDestroysTheLibrary()
    {
        var log = new RecordingLogger<AccountAdminService>();
        var (admin, _, _, connection) = await BuildAsync(log: log);

        using (connection)
        {
            admin.ResetEverything();
        }

        Assert.True(log.Logged("reset requested"));
        Assert.True(log.Logged("reset finished"));
    }
}
