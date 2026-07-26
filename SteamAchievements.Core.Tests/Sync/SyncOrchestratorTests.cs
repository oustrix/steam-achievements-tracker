using System.Net;
using Polly.CircuitBreaker;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Sync;
using SteamAchievements.Core.Tests.Steam;

namespace SteamAchievements.Core.Tests.Sync;

public class SyncOrchestratorTests
{
    private static readonly ulong SteamId = 76561190000000002;

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static HttpResponseMessage NoStats() =>
        new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("<html><body>Requested app has no stats</body></html>",
                System.Text.Encoding.UTF8, "text/html"),
        };

    private static async Task<(SyncOrchestrator Sync, GameRepository Repo, FakeHttpMessageHandler Handler)> Build()
    {
        var owned = await File.ReadAllTextAsync(TestPaths.Data("owned_games.json"));
        var schema = await File.ReadAllTextAsync(TestPaths.Data("schema_for_game.json"));
        var player = await File.ReadAllTextAsync(TestPaths.Data("player_achievements.json"));
        var global = await File.ReadAllTextAsync(TestPaths.Data("global_percentages.json"));

        var handler = new FakeHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("GetOwnedGames")) return Json(owned);
            if (url.Contains("GetSchemaForGame")) return url.Contains("appid=220") ? NoStats() : Json(schema);
            if (url.Contains("GetPlayerAchievements")) return url.Contains("appid=220") ? NoStats() : Json(player);
            if (url.Contains("GetGlobalAchievementPercentages")) return Json(global);

            throw new InvalidOperationException($"Unexpected request: {url}");
        });

        var client = new SteamApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY");
        var repository = new GameRepository(Database.Open(":memory:"));

        return (new SyncOrchestrator(client, repository, SyncOptions.Default), repository, handler);
    }

    [Fact]
    public async Task StoresLibraryAndAchievementsOnFirstRun()
    {
        var (sync, repository, _) = await Build();

        await sync.RunAsync(SteamId, force: false, progress: null, CancellationToken.None);

        Assert.Equal(2, repository.GetOwnedGames().Count);
        Assert.Equal(2, repository.GetGameProgress(292030).Count);

        var snapshots = Dapper.SqlMapper.QuerySingle<long>(
            repository.Connection, "SELECT COUNT(*) FROM snapshots");
        Assert.Equal(1, snapshots);
    }

    [Fact]
    public async Task MarksGamesWithoutAchievementsAndStopsQueryingThem()
    {
        var (sync, repository, handler) = await Build();

        await sync.RunAsync(SteamId, force: false, progress: null, CancellationToken.None);
        var requestsAfterFirst = handler.Requests.Count;
        await sync.RunAsync(SteamId, force: false, progress: null, CancellationToken.None);

        Assert.False(repository.GetSyncStates()[220].HasAchievements);

        // Second run must only re-fetch the library itself.
        Assert.Equal(requestsAfterFirst + 1, handler.Requests.Count);
    }

    [Fact]
    public async Task ReportsProgressForEveryProcessedGame()
    {
        var (sync, _, _) = await Build();
        var reports = new List<SyncProgress>();

        await sync.RunAsync(SteamId, force: false, new Progress<SyncProgress>(reports.Add), CancellationToken.None);

        await Task.Delay(50); // Progress<T> marshals asynchronously
        Assert.NotEmpty(reports);
        Assert.Equal(reports[^1].Total, reports[^1].Completed);
    }

    [Fact]
    public async Task StopsWhenCancelled()
    {
        var (sync, _, _) = await Build();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sync.RunAsync(SteamId, force: false, progress: null, cancellation.Token));
    }

    [Fact]
    public async Task DoesNotRetryOnInvalidKey()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("<html><body>Access is denied.</body></html>",
                System.Text.Encoding.UTF8, "text/html"),
        });

        var sync = new SyncOrchestrator(
            new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "BAD"),
            new GameRepository(Database.Open(":memory:")),
            SyncOptions.Default);

        await Assert.ThrowsAsync<SteamApiException>(
            () => sync.RunAsync(SteamId, force: false, progress: null, CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RetriesTransientFailureThenSucceeds()
    {
        var owned = await File.ReadAllTextAsync(TestPaths.Data("owned_games.json"));
        var ownedAttempts = 0;

        var handler = new FakeHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("GetOwnedGames"))
            {
                ownedAttempts++;
                return ownedAttempts <= 2
                    ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    {
                        Content = new StringContent("<html><body>Rate limited</body></html>",
                            System.Text.Encoding.UTF8, "text/html"),
                    }
                    : Json(owned);
            }

            // Every per-game request answers "no stats" so the sync completes
            // quickly without exercising anything beyond the retry path under test.
            return NoStats();
        });

        var sync = new SyncOrchestrator(
            new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY"),
            new GameRepository(Database.Open(":memory:")),
            SyncOptions.Default,
            retryBaseDelay: TimeSpan.FromMilliseconds(1));

        await sync.RunAsync(SteamId, force: false, progress: null, CancellationToken.None);

        // Two 429s then a 200: proves the transient failure was retried
        // rather than surfaced to the caller immediately.
        Assert.Equal(3, ownedAttempts);

        // 3 attempts for GetOwnedGames + 1 schema request per game in the
        // library (both answer "no stats" and stop there).
        Assert.Equal(3 + 2, handler.Requests.Count);
    }

    [Fact]
    public async Task TripsCircuitBreakerAfterSustainedFailuresAndStopsIssuingRequests()
    {
        // Deterministic only when exercised serially: GetOwnedGamesAsync is
        // the single call made before any parallel worker dispatch, so
        // driving it to fail persistently pins exactly how many HTTP
        // attempts happen before the breaker opens, and then proves a
        // second RunAsync call is rejected without touching the network at
        // all — the observable effect of "the breaker stops issuing
        // requests" rather than "retry eventually gives up" (which the
        // first call alone cannot distinguish, since MaxRetryAttempts also
        // bounds it at 5).
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html><body>Steam is down</body></html>",
                System.Text.Encoding.UTF8, "text/html"),
        });

        var sync = new SyncOrchestrator(
            new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY"),
            new GameRepository(Database.Open(":memory:")),
            SyncOptions.Default,
            retryBaseDelay: TimeSpan.FromMilliseconds(1));

        // First call: 1 initial attempt + 4 retries, all failing with a
        // transient 500 — exactly MinimumThroughput=5 failures at
        // FailureRatio=1.0, which trips the breaker right as retry exhausts
        // its own budget.
        await Assert.ThrowsAsync<SteamApiException>(
            () => sync.RunAsync(SteamId, force: false, progress: null, CancellationToken.None));
        Assert.Equal(5, handler.Requests.Count);

        // Second call: BreakDuration is 30s, far longer than this test, so
        // the breaker is still open. Its very first attempt must be
        // rejected by the breaker itself — no additional HTTP request is
        // issued at all.
        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => sync.RunAsync(SteamId, force: false, progress: null, CancellationToken.None));
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task PropagatesInvalidKeyDiscoveredMidSyncDirectly()
    {
        // A single-game library: Parallel.ForEachAsync's worker body then
        // runs exactly once, so if it throws there is exactly one exception
        // for the loop to propagate — no risk of a sibling iteration
        // contributing a second exception (e.g. via cancellation) and
        // forcing an AggregateException, which would defeat the point of
        // this test.
        const string singleGameOwned = """
            {
              "response": {
                "game_count": 1,
                "games": [
                  { "appid": 400, "name": "Portal", "playtime_forever": 500,
                    "img_icon_url": "abc", "rtime_last_played": 1750000000 }
                ]
              }
            }
            """;

        var handler = new FakeHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("GetOwnedGames")) return Json(singleGameOwned);

            // The key was accepted for GetOwnedGames but rejected here — an
            // invalid key discovered mid-sync, inside a worker, not at the
            // initial library fetch.
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("<html><body>Access is denied.</body></html>",
                    System.Text.Encoding.UTF8, "text/html"),
            };
        });

        var sync = new SyncOrchestrator(
            new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY"),
            new GameRepository(Database.Open(":memory:")),
            SyncOptions.Default);

        // Pins that Parallel.ForEachAsync surfaces a single loop-body
        // exception as-is, not wrapped in an AggregateException — that
        // unwrapping is what lets callers write `catch (SteamApiException)`
        // directly around RunAsync.
        var exception = await Assert.ThrowsAsync<SteamApiException>(
            () => sync.RunAsync(SteamId, force: false, progress: null, CancellationToken.None));

        Assert.Equal(SteamApiErrorKind.InvalidKey, exception.Kind);
    }
}
