using System.Net;
using System.Text.Json;

namespace SteamAchievements.Core.Steam;

public sealed class SteamApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly string _apiKey;

    public SteamApiClient(HttpClient http, string apiKey)
    {
        _http = http;
        _apiKey = apiKey;
    }

    // The following four methods are provisional stubs. They exist only to let
    // Task 4's error-classification tests exercise the shared send-and-classify
    // path below. Task 5 replaces their bodies with real parsing into typed
    // records (OwnedGame, AchievementSchema, PlayerAchievement, global
    // percentages) — do not build on these signatures elsewhere yet.

    public Task<JsonDocument> GetOwnedGamesAsync(ulong steamId, CancellationToken cancellationToken) =>
        GetJsonAsync<JsonDocument>(
            $"IPlayerService/GetOwnedGames/v1/?key={_apiKey}&steamid={steamId}&include_appinfo=1&include_played_free_games=1",
            cancellationToken);

    public Task<JsonDocument> GetSchemaForGameAsync(uint appId, CancellationToken cancellationToken) =>
        GetJsonAsync<JsonDocument>(
            $"ISteamUserStats/GetSchemaForGame/v2/?key={_apiKey}&appid={appId}",
            cancellationToken);

    public Task<JsonDocument> GetPlayerAchievementsAsync(ulong steamId, uint appId, CancellationToken cancellationToken) =>
        GetJsonAsync<JsonDocument>(
            $"ISteamUserStats/GetPlayerAchievements/v1/?key={_apiKey}&steamid={steamId}&appid={appId}",
            cancellationToken);

    public Task<JsonDocument> GetGlobalPercentagesAsync(uint appId, CancellationToken cancellationToken) =>
        GetJsonAsync<JsonDocument>(
            $"ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={appId}",
            cancellationToken);

    internal async Task<T> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(path, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw Classify(response.StatusCode, body);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                ?? throw new SteamApiException(SteamApiErrorKind.Unknown, (int)response.StatusCode,
                    "Steam returned an empty document.");
        }
        catch (JsonException)
        {
            // A 200 with a non-JSON body means Steam served an error or an
            // interstitial page. Never surface a raw JsonException.
            throw new SteamApiException(SteamApiErrorKind.Unknown, (int)response.StatusCode,
                "Steam returned a non-JSON response.");
        }
    }

    private static SteamApiException Classify(HttpStatusCode status, string body)
    {
        // Bodies are HTML, not JSON — match on text, and never echo the body
        // back, because the request URL it may contain carries the API key.
        if (body.Contains("has no stats", StringComparison.OrdinalIgnoreCase))
        {
            return new SteamApiException(SteamApiErrorKind.NoStatsForApp, (int)status,
                "The requested app has no achievements.");
        }

        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest =>
                new SteamApiException(SteamApiErrorKind.InvalidKey, (int)status,
                    "Steam rejected the API key. Check it in settings."),

            HttpStatusCode.TooManyRequests =>
                new SteamApiException(SteamApiErrorKind.RateLimited, (int)status,
                    "Steam is rate limiting this key."),

            >= HttpStatusCode.InternalServerError =>
                new SteamApiException(SteamApiErrorKind.ServerError, (int)status,
                    $"Steam returned {(int)status}."),

            _ => new SteamApiException(SteamApiErrorKind.Unknown, (int)status,
                $"Unexpected response {(int)status} from Steam."),
        };
    }
}
