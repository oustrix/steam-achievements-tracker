using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SteamAchievements.Core.App;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Tests.Steam;

namespace SteamAchievements.Core.Tests.App;

public class OnboardingServiceTests
{
    private static readonly ulong SteamId = 76561190000000002;
    private const string Key = "0123456789abcdef0123456789abcdef";

    /// <param name="keyCheck">
    /// How Steam answers the trial GetOwnedGames request that validates a
    /// submitted key. Defaults to the recorded success fixture.
    /// </param>
    private static async Task<(OnboardingService Service, MemorySecretStore Secrets, IAccountStore Accounts, Microsoft.Data.Sqlite.SqliteConnection Connection)>
        BuildAsync(
            HttpStatusCode status = HttpStatusCode.OK,
            string? body = null,
            FakeHttpMessageHandler? keyCheck = null)
    {
        body ??= await File.ReadAllTextAsync(TestPaths.Data("profile_public.xml"));

        var connection = Database.Open(":memory:");
        var accounts = new SqliteAccountStore(connection);
        var secrets = new MemorySecretStore();

        var community = new SteamCommunityClient(
            new HttpClient(FakeHttpMessageHandler.Returning(status, body, "text/xml"))
            {
                BaseAddress = new Uri("https://steamcommunity.com/"),
            });

        keyCheck ??= FakeHttpMessageHandler.Returning(
            HttpStatusCode.OK, await File.ReadAllTextAsync(TestPaths.Data("owned_games.json")));

        SteamApiClient ClientFor(string key) =>
            new(new HttpClient(keyCheck) { BaseAddress = new Uri("https://api.steampowered.com/") }, key);

        return (
            new OnboardingService(
                accounts, secrets, new SteamAccountLocator(new FixedSteamPath(null)), community, ClientFor,
                NullLogger<OnboardingService>.Instance),
            secrets, accounts, connection);
    }

    [Fact]
    public async Task StartsAtAccountSelection()
    {
        var (service, _, _, connection) = await BuildAsync();
        using (connection)
        {
            Assert.Equal(OnboardingStep.ChooseAccount, service.Step);
        }
    }

    [Fact]
    public async Task StoresTheChosenAccountWithItsNameAndAvatar()
    {
        var (service, _, accounts, connection) = await BuildAsync();
        using (connection)
        {
            await service.ChooseAccountAsync(SteamId, CancellationToken.None);

            var stored = accounts.Current;
            Assert.NotNull(stored);
            Assert.Equal(SteamId, stored.SteamId64);
            Assert.Equal("oustrix", stored.PersonaName);
            Assert.EndsWith("_full.jpg", stored.AvatarUrl);
        }
    }

    [Fact]
    public async Task StoresTheAccountEvenWhenTheProfileLookupFails()
    {
        var (service, _, accounts, connection) = await BuildAsync(HttpStatusCode.ServiceUnavailable, string.Empty);
        using (connection)
        {
            await service.ChooseAccountAsync(SteamId, CancellationToken.None);

            Assert.Equal(SteamId, accounts.Current!.SteamId64);
            Assert.Equal(string.Empty, accounts.Current.PersonaName);
        }
    }

    [Fact]
    public async Task MovesToTheKeyStepOnceAnAccountIsChosen()
    {
        var (service, _, _, connection) = await BuildAsync();
        using (connection)
        {
            await service.ChooseAccountAsync(SteamId, CancellationToken.None);

            Assert.Equal(OnboardingStep.EnterKey, service.Step);
        }
    }

    [Fact]
    public async Task StoresANormalizedKeyOnceSteamAcceptsItAndFinishesOnboarding()
    {
        var (service, secrets, _, connection) = await BuildAsync();
        using (connection)
        {
            await service.ChooseAccountAsync(SteamId, CancellationToken.None);

            Assert.Equal(KeySubmission.Accepted, await service.SubmitKeyAsync($"  {Key}  ", CancellationToken.None));
            Assert.Equal(Key.ToUpperInvariant(), secrets.Read());
            Assert.Equal(OnboardingStep.Ready, service.Step);
        }
    }

    [Fact]
    public async Task RejectsAMalformedKeyWithoutSpendingARequest()
    {
        var keyCheck = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("must not be called"));
        var (service, secrets, _, connection) = await BuildAsync(keyCheck: keyCheck);
        using (connection)
        {
            await service.ChooseAccountAsync(SteamId, CancellationToken.None);

            Assert.Equal(KeySubmission.Malformed, await service.SubmitKeyAsync("nope", CancellationToken.None));
            Assert.Null(secrets.Read());
            Assert.Empty(keyCheck.Requests);
            Assert.Equal(OnboardingStep.EnterKey, service.Step);
        }
    }

    [Fact]
    public async Task DoesNotStoreAWellFormedKeySteamRefuses()
    {
        var html = await File.ReadAllTextAsync(TestPaths.Data("error_unauthorized.html"));
        var (service, secrets, _, connection) = await BuildAsync(
            keyCheck: FakeHttpMessageHandler.Returning(HttpStatusCode.Unauthorized, html, "text/html"));

        using (connection)
        {
            await service.ChooseAccountAsync(SteamId, CancellationToken.None);

            Assert.Equal(KeySubmission.Rejected, await service.SubmitKeyAsync(Key, CancellationToken.None));
            Assert.Null(secrets.Read());
            Assert.Equal(OnboardingStep.EnterKey, service.Step);
        }
    }

    [Fact]
    public async Task ReportsAnUnreachableSteamSeparatelyFromARefusedKey()
    {
        var (service, secrets, _, connection) = await BuildAsync(
            keyCheck: new FakeHttpMessageHandler(_ => throw new HttpRequestException("no route")));

        using (connection)
        {
            await service.ChooseAccountAsync(SteamId, CancellationToken.None);

            Assert.Equal(KeySubmission.Unreachable, await service.SubmitKeyAsync(Key, CancellationToken.None));
            Assert.Null(secrets.Read());
        }
    }

    [Fact]
    public async Task AcceptingAKeyClearsAnEarlierRejection()
    {
        var (service, _, accounts, connection) = await BuildAsync();
        using (connection)
        {
            await service.ChooseAccountAsync(SteamId, CancellationToken.None);
            accounts.MarkKeyRejected(DateTimeOffset.UtcNow);

            await service.SubmitKeyAsync(Key, CancellationToken.None);

            Assert.Null(accounts.KeyRejectedAt);
        }
    }

    [Fact]
    public async Task RefusesToValidateAKeyBeforeAnAccountIsChosen()
    {
        var (service, _, _, connection) = await BuildAsync();
        using (connection)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SubmitKeyAsync(Key, CancellationToken.None));
        }
    }

    [Fact]
    public async Task FindsNoAccountsWhenSteamIsAbsentSoTheScreenFallsBackToManualEntry()
    {
        var (service, _, _, connection) = await BuildAsync();
        using (connection)
        {
            Assert.Empty(service.DiscoveredAccounts);
        }
    }
}
