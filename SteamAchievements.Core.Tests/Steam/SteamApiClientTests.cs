using System.Net;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Steam;

public class SteamApiClientTests
{
    private static SteamApiClient Client(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY");

    [Fact]
    public async Task ClassifiesHtmlUnauthorizedBodyAsInvalidKey()
    {
        var html = await File.ReadAllTextAsync(TestPaths.Data("error_unauthorized.html"));
        var client = Client(FakeHttpMessageHandler.Returning(HttpStatusCode.Unauthorized, html, "text/html"));

        var error = await Assert.ThrowsAsync<SteamApiException>(
            () => client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None));

        Assert.Equal(SteamApiErrorKind.InvalidKey, error.Kind);
    }

    [Fact]
    public async Task ClassifiesBadRequestWithoutKeyAsInvalidKey()
    {
        var client = Client(FakeHttpMessageHandler.Returning(
            HttpStatusCode.BadRequest, "<html><head><title>Bad Request</title></head></html>", "text/html"));

        var error = await Assert.ThrowsAsync<SteamApiException>(
            () => client.GetSchemaForGameAsync(292030, CancellationToken.None));

        Assert.Equal(SteamApiErrorKind.InvalidKey, error.Kind);
    }

    [Fact]
    public async Task ClassifiesNoStatsResponseAsNoStatsForApp()
    {
        var client = Client(FakeHttpMessageHandler.Returning(
            HttpStatusCode.BadRequest,
            "<html><head><title>Bad Request</title></head><body><h1>Bad Request</h1>Requested app has no stats</body></html>",
            "text/html"));

        var error = await Assert.ThrowsAsync<SteamApiException>(
            () => client.GetPlayerAchievementsAsync(76561190000000002, 220, CancellationToken.None));

        Assert.Equal(SteamApiErrorKind.NoStatsForApp, error.Kind);
    }

    [Fact]
    public async Task ClassifiesTooManyRequestsAsRateLimited()
    {
        var client = Client(FakeHttpMessageHandler.Returning(HttpStatusCode.TooManyRequests, string.Empty, "text/plain"));

        var error = await Assert.ThrowsAsync<SteamApiException>(
            () => client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None));

        Assert.Equal(SteamApiErrorKind.RateLimited, error.Kind);
    }

    [Fact]
    public async Task ClassifiesServerErrorAsServerError()
    {
        var client = Client(FakeHttpMessageHandler.Returning(HttpStatusCode.ServiceUnavailable, string.Empty, "text/plain"));

        var error = await Assert.ThrowsAsync<SteamApiException>(
            () => client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None));

        Assert.Equal(SteamApiErrorKind.ServerError, error.Kind);
    }

    [Fact]
    public async Task NeverLeaksTheApiKeyIntoExceptionMessages()
    {
        var client = Client(FakeHttpMessageHandler.Returning(HttpStatusCode.Unauthorized, "denied", "text/html"));

        var error = await Assert.ThrowsAsync<SteamApiException>(
            () => client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None));

        Assert.DoesNotContain("TESTKEY", error.Message);
        Assert.DoesNotContain("TESTKEY", error.ToString());
    }

    [Fact]
    public async Task ClassifiesTransportFailureAsTransientServerError()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"));
        var client = Client(handler);

        var error = await Assert.ThrowsAsync<SteamApiException>(
            () => client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None));

        Assert.Equal(SteamApiErrorKind.ServerError, error.Kind);
        Assert.True(error.IsTransient);
    }

    [Fact]
    public async Task PropagatesGenuineCancellationWithoutWrapping()
    {
        var client = Client(FakeHttpMessageHandler.Returning(HttpStatusCode.OK, "{}"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetOwnedGamesAsync(76561190000000002, cts.Token));
    }

    [Fact]
    public async Task DoesNotClassifyNoStatsBodyOutsideBadRequestAsNoStatsForApp()
    {
        var client = Client(FakeHttpMessageHandler.Returning(
            HttpStatusCode.TooManyRequests,
            "<html><body>Requested app has no stats</body></html>",
            "text/html"));

        var error = await Assert.ThrowsAsync<SteamApiException>(
            () => client.GetPlayerAchievementsAsync(76561190000000002, 220, CancellationToken.None));

        Assert.Equal(SteamApiErrorKind.RateLimited, error.Kind);
    }
}
