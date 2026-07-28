using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Sync;
using SteamAchievements.Core.Tests.Steam;

namespace SteamAchievements.Core.Tests.Sync;

public class SyncOrchestratorTests
{
    private static readonly ulong SteamId = 76561190000000002;

    // SteamApiClient now rate-limits every outgoing request intrinsically
    // (~5/s in production). These tests drive several requests through one
    // client to exercise retry/circuit-breaker/multi-worker behavior, so they
    // inject a limiter fast enough to be a no-op — otherwise the suite would
    // start sleeping in real wall-clock time for a concern these tests aren't
    // about.
    private static RateLimiter NoRateLimit() => new(requestsPerSecond: 1_000_000);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static HttpResponseMessage NoStats() =>
        new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("<html><body>Requested app has no stats</body></html>",
                System.Text.Encoding.UTF8, "text/html"),
        };

    private static async Task<(SyncOrchestrator Sync, GameRepository Repo, FakeHttpMessageHandler Handler)>
        Build(ILogger<SyncOrchestrator>? log = null)
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
            new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY", NoRateLimit());
        var repository = new GameRepository(Database.Open(":memory:"));

        return (
            new SyncOrchestrator(
                client, repository, SyncOptions.Default, log ?? NullLogger<SyncOrchestrator>.Instance),
            repository, handler);
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

        // Progress<T> marshals each report asynchronously (via the captured
        // SynchronizationContext, or a ThreadPool post if none was captured),
        // so with WorkerCount workers reporting concurrently, callbacks can
        // run on several threads at once — a plain List<T> is not safe under
        // concurrent Add.
        var reports = new System.Collections.Concurrent.ConcurrentQueue<SyncProgress>();

        await sync.RunAsync(SteamId, force: false, new Progress<SyncProgress>(reports.Enqueue), CancellationToken.None);

        // Reports can still be in flight after RunAsync returns, and their
        // arrival order across threads isn't guaranteed, so poll for the
        // highest Completed value to reach Total rather than assuming the
        // last-enqueued report is the final one.
        var deadline = DateTime.UtcNow.AddSeconds(1);
        while ((reports.IsEmpty || reports.Max(r => r.Completed) != reports.Max(r => r.Total))
               && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.NotEmpty(reports);
        Assert.Equal(reports.Max(r => r.Total), reports.Max(r => r.Completed));
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
            new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "BAD", NoRateLimit()),
            new GameRepository(Database.Open(":memory:")),
            SyncOptions.Default,
            NullLogger<SyncOrchestrator>.Instance);

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
            new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY", NoRateLimit()),
            new GameRepository(Database.Open(":memory:")),
            SyncOptions.Default,
            NullLogger<SyncOrchestrator>.Instance,
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
            new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY", NoRateLimit()),
            new GameRepository(Database.Open(":memory:")),
            SyncOptions.Default,
            NullLogger<SyncOrchestrator>.Instance,
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
            new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY", NoRateLimit()),
            new GameRepository(Database.Open(":memory:")),
            SyncOptions.Default,
            NullLogger<SyncOrchestrator>.Instance);

        // Pins that Parallel.ForEachAsync surfaces a single loop-body
        // exception as-is, not wrapped in an AggregateException — that
        // unwrapping is what lets callers write `catch (SteamApiException)`
        // directly around RunAsync.
        var exception = await Assert.ThrowsAsync<SteamApiException>(
            () => sync.RunAsync(SteamId, force: false, progress: null, CancellationToken.None));

        Assert.Equal(SteamApiErrorKind.InvalidKey, exception.Kind);
    }

    [Fact]
    public async Task PropagatesInvalidKeyDiscoveredMidSyncAcrossMultipleWorkers()
    {
        // 8 never-synced games against WorkerCount=4 workers: every worker
        // is in flight when the key gets rejected, so several of them
        // discover SteamApiException(InvalidKey) concurrently.
        // Parallel.ForEachAsync would otherwise wrap those into an
        // AggregateException, which defeats a caller's
        // `catch (SteamApiException e) when (e.Kind == InvalidKey)` — the
        // one actionable message in the whole onboarding flow.
        var games = string.Join(",", Enumerable.Range(1, 8).Select(i => $$"""
            { "appid": {{400 + i}}, "name": "Game {{i}}", "playtime_forever": 100,
              "img_icon_url": "abc", "rtime_last_played": 1750000000 }
            """));

        var multiGameOwned = $$"""
            {
              "response": {
                "game_count": 8,
                "games": [{{games}}]
              }
            }
            """;

        var handler = new FakeHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("GetOwnedGames")) return Json(multiGameOwned);

            // Every per-game call rejects the key — since none of these
            // games have synced before, schema is the first call each
            // worker makes, so several workers hit this at once.
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("<html><body>Access is denied.</body></html>",
                    System.Text.Encoding.UTF8, "text/html"),
            };
        });

        var sync = new SyncOrchestrator(
            new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY", NoRateLimit()),
            new GameRepository(Database.Open(":memory:")),
            SyncOptions.Default,
            NullLogger<SyncOrchestrator>.Instance);

        // Pins that even with multiple concurrent workers discovering the
        // same failure, RunAsync surfaces a bare SteamApiException — not an
        // AggregateException — so a caller can catch it directly.
        var exception = await Assert.ThrowsAsync<SteamApiException>(
            () => sync.RunAsync(SteamId, force: false, progress: null, CancellationToken.None));

        Assert.Equal(SteamApiErrorKind.InvalidKey, exception.Kind);
    }

    [Fact]
    public async Task LogsThePlanSizeAndEachGamesOutcome()
    {
        var log = new RecordingLogger<SyncOrchestrator>();
        var (orchestrator, repository, _) = await Build(log);

        await orchestrator.RunAsync(SteamId, force: true, null, CancellationToken.None);

        Assert.True(log.Logged("plan:"));
        Assert.Contains(log.Lines, line => line.Contains("synced", StringComparison.Ordinal));

        // appid 220 is the fixture's no-stats game.
        Assert.True(log.Logged("game 220 has no achievements"));
    }
}
