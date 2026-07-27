using System.Net;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Steam;

public class SteamApiEndpointTests
{
    private static async Task<(SteamApiClient Client, FakeHttpMessageHandler Handler)> ClientFor(
        string fixture, string apiKey = TestSteamApiClientFactory.ApiKey)
    {
        var body = await File.ReadAllTextAsync(TestPaths.Data(fixture));
        return TestSteamApiClientFactory.Create(FakeHttpMessageHandler.Returning(HttpStatusCode.OK, body), apiKey);
    }

    [Fact]
    public async Task ParsesOwnedGames()
    {
        var (client, _) = await ClientFor("owned_games.json");

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
        var (client, handler) = await ClientFor("owned_games.json");

        await client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None);

        var url = handler.Requests.Single().ToString();
        Assert.Contains("include_appinfo=1", url);
        Assert.Contains("include_played_free_games=1", url);
    }

    [Fact]
    public async Task ReturnsEmptyListForPrivateProfile()
    {
        var (client, _) = await ClientFor("private_profile.json");

        var games = await client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None);

        Assert.Empty(games);
    }

    [Fact]
    public async Task ParsesAchievementSchemaPreservingOrder()
    {
        var (client, _) = await ClientFor("schema_for_game.json");

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
        var (client, _) = await ClientFor("player_achievements.json");

        var progress = await client.GetPlayerAchievementsAsync(76561190000000002, 292030, CancellationToken.None);

        Assert.True(progress[0].Unlocked);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), progress[0].UnlockedAt);
        Assert.False(progress[1].Unlocked);
        Assert.Null(progress[1].UnlockedAt);
    }

    [Fact]
    public async Task RequestsGlobalPercentagesUsingGameIdParameter()
    {
        var (client, handler) = await ClientFor("global_percentages.json");

        var percentages = await client.GetGlobalPercentagesAsync(292030, CancellationToken.None);

        var url = handler.Requests.Single().ToString();
        Assert.Contains("gameid=292030", url);       // NOT appid — the only endpoint that differs
        Assert.DoesNotContain("key=", url);          // this endpoint needs no key

        // The fixture carries Steam's real wire format, where percent is a
        // QUOTED string. A synthetic fixture using bare numbers hid this and
        // let a live sync lose rarity data for every game in the library.
        Assert.Equal(70.5, percentages["ACH_1"], 1);
        Assert.Equal(0.4, percentages["ACH_2"], 1);
    }

    [Fact]
    public async Task EscapesApiKeyContainingAmpersandSoOtherParametersSurviveIntact()
    {
        var (client, handler) = await ClientFor("owned_games.json", "AB&steamid=999&evil=1");

        await client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None);

        // PathAndQuery is what actually reaches the wire — ToString() would
        // misleadingly retain a URI fragment that HTTP never transmits.
        var url = handler.Requests.Single().PathAndQuery;
        Assert.Contains("steamid=76561190000000002", url);
        Assert.Contains("include_appinfo=1", url);
        Assert.Contains("include_played_free_games=1", url);
        Assert.DoesNotContain("evil=1", url);
    }

    [Fact]
    public async Task EscapesApiKeyContainingFragmentSoTrailingParametersAreNotDropped()
    {
        var (client, handler) = await ClientFor("owned_games.json", "AB#fragment&steamid=999");

        await client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None);

        // PathAndQuery excludes the fragment entirely, so this is the only way
        // to see whether the '#' truncated the actual outgoing request.
        var url = handler.Requests.Single().PathAndQuery;
        Assert.Contains("steamid=76561190000000002", url);
        Assert.Contains("include_appinfo=1", url);
        Assert.Contains("include_played_free_games=1", url);
    }

    [Fact]
    public async Task TrimsWhitespacePastedIntoTheApiKey()
    {
        var (client, handler) = await ClientFor("owned_games.json", "  TESTKEY\n");

        await client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None);

        var url = handler.Requests.Single().PathAndQuery;
        Assert.Contains("key=TESTKEY&", url);
    }

    [Fact]
    public async Task DeduplicatesGlobalPercentagesByKeepingFirstOccurrenceInsteadOfThrowing()
    {
        var (client, _) = TestSteamApiClientFactory.Create(FakeHttpMessageHandler.Returning(HttpStatusCode.OK,
            """
            {
              "achievementpercentages": {
                "achievements": [
                  { "name": "ACH_1", "percent": 62.4 },
                  { "name": "ACH_1", "percent": 10.0 }
                ]
              }
            }
            """));

        var percentages = await client.GetGlobalPercentagesAsync(292030, CancellationToken.None);

        Assert.Single(percentages);
        Assert.Equal(62.4, percentages["ACH_1"], 1);
    }
}
