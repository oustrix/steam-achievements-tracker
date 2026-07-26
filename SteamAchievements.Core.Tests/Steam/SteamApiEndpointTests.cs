using System.Net;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Steam;

public class SteamApiEndpointTests
{
    private static async Task<SteamApiClient> ClientFor(string fixture, FakeHttpMessageHandler? capture = null)
    {
        var body = await File.ReadAllTextAsync(TestPaths.Data(fixture));
        var handler = capture ?? FakeHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        return new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY");
    }

    [Fact]
    public async Task ParsesOwnedGames()
    {
        var client = await ClientFor("owned_games.json");

        var games = await client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None);

        Assert.Equal(2, games.Count);
        Assert.Equal("The Witcher 3: Wild Hunt", games[0].Name);
        Assert.Equal(6420, games[0].PlaytimeForever);
        Assert.Equal(120, games[0].PlaytimeTwoWeeks);
        Assert.Null(games[1].LastPlayed);
    }

    [Fact]
    public async Task RequestsOwnedGamesWithAppInfoIncluded()
    {
        var body = await File.ReadAllTextAsync(TestPaths.Data("owned_games.json"));
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        var client = new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY");

        await client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None);

        var url = handler.Requests.Single().ToString();
        Assert.Contains("include_appinfo=1", url);
        Assert.Contains("include_played_free_games=1", url);
    }

    [Fact]
    public async Task ReturnsEmptyListForPrivateProfile()
    {
        var client = await ClientFor("private_profile.json");

        var games = await client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None);

        Assert.Empty(games);
    }

    [Fact]
    public async Task ParsesAchievementSchemaPreservingOrder()
    {
        var client = await ClientFor("schema_for_game.json");

        var schema = await client.GetSchemaForGameAsync(292030, CancellationToken.None);

        Assert.Equal(2, schema.Count);
        Assert.Equal(0, schema[0].SortOrder);
        Assert.Equal(1, schema[1].SortOrder);
        Assert.True(schema[1].IsHidden);
        Assert.Equal(string.Empty, schema[1].Description);
    }

    [Fact]
    public async Task ParsesPlayerAchievements()
    {
        var client = await ClientFor("player_achievements.json");

        var progress = await client.GetPlayerAchievementsAsync(76561190000000002, 292030, CancellationToken.None);

        Assert.True(progress[0].Unlocked);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), progress[0].UnlockedAt);
        Assert.False(progress[1].Unlocked);
        Assert.Null(progress[1].UnlockedAt);
    }

    [Fact]
    public async Task RequestsGlobalPercentagesUsingGameIdParameter()
    {
        var body = await File.ReadAllTextAsync(TestPaths.Data("global_percentages.json"));
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        var client = new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY");

        var percentages = await client.GetGlobalPercentagesAsync(292030, CancellationToken.None);

        var url = handler.Requests.Single().ToString();
        Assert.Contains("gameid=292030", url);       // NOT appid — the only endpoint that differs
        Assert.DoesNotContain("key=", url);          // this endpoint needs no key
        Assert.Equal(62.4, percentages["ACH_1"], 1);
        Assert.Equal(0.4, percentages["ACH_2"], 1);
    }
}
