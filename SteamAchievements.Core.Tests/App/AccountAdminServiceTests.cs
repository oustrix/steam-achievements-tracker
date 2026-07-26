using System.Net;
using SteamAchievements.Core.Abstractions;
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
    private static readonly ulong ActiveInFixture = 76561190000000002;

    private sealed class MemorySecretStore : ISecretStore
    {
        public string? Secret { get; private set; } = "0123456789ABCDEF0123456789ABCDEF";

        public string? Read() => Secret;

        public void Write(string secret) => Secret = secret;

        public void Clear() => Secret = null;
    }

    private sealed class FixedPath(string? path) : ISteamPathProvider
    {
        public string? FindSteamPath() => path;
    }

    private static string SteamRootWithFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.Copy(TestPaths.Data("loginusers.vdf"), Path.Combine(root, "config", "loginusers.vdf"));
        return root;
    }

    private static async Task<(AccountAdminService Admin, MemorySecretStore Secrets, IAccountStore Accounts, Microsoft.Data.Sqlite.SqliteConnection Connection)>
        BuildAsync(string? steamPath = null)
    {
        var body = await File.ReadAllTextAsync(TestPaths.Data("profile_public.xml"));
        var connection = Database.Open(":memory:");
        var accounts = new SqliteAccountStore(connection);
        accounts.Set(Stored, "oustrix", "avatar");

        var repository = new GameRepository(connection);
        repository.UpsertOwnedGames([new OwnedGame(220, "Half-Life 2", "hash", 600, 0, null)]);

        var secrets = new MemorySecretStore();
        var community = new SteamCommunityClient(
            new HttpClient(FakeHttpMessageHandler.Returning(HttpStatusCode.OK, body, "text/xml"))
            {
                BaseAddress = new Uri("https://steamcommunity.com/"),
            });

        var admin = new AccountAdminService(
            connection, accounts, secrets, new SteamAccountLocator(new FixedPath(steamPath)), community);

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
        var root = SteamRootWithFixture();
        try
        {
            var (admin, _, _, connection) = await BuildAsync(root);
            using (connection)
            {
                Assert.NotNull(admin.Mismatch);
                Assert.Equal(ActiveInFixture, admin.Mismatch.ActiveSteamId64);
                Assert.Equal("currentuser", admin.Mismatch.ActiveAccountName);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsNoMismatchWhenSteamIsSignedInAsTheStoredAccount()
    {
        var root = SteamRootWithFixture();
        try
        {
            var (admin, _, accounts, connection) = await BuildAsync(root);
            using (connection)
            {
                accounts.Set(ActiveInFixture, "oustrix", "avatar");

                Assert.Null(admin.Mismatch);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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
}
