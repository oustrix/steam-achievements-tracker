using System.Net;
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
}
