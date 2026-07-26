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

    public async Task<IReadOnlyList<OwnedGame>> GetOwnedGamesAsync(ulong steamId, CancellationToken cancellationToken)
    {
        var envelope = await GetJsonAsync<OwnedGamesEnvelope>(
            $"IPlayerService/GetOwnedGames/v1/?key={_apiKey}&steamid={steamId}" +
            "&include_appinfo=1&include_played_free_games=1", cancellationToken);

        // A private profile answers 200 with an empty response object.
        return envelope.Response?.Games?
            .Select(g => new OwnedGame(
                g.AppId,
                g.Name ?? string.Empty,
                g.IconHash ?? string.Empty,
                g.PlaytimeForever,
                g.PlaytimeTwoWeeks,
                g.LastPlayed > 0 ? DateTimeOffset.FromUnixTimeSeconds(g.LastPlayed) : null))
            .ToList()
            ?? [];
    }

    public async Task<IReadOnlyList<AchievementSchema>> GetSchemaForGameAsync(uint appId, CancellationToken cancellationToken)
    {
        var envelope = await GetJsonAsync<SchemaEnvelope>(
            $"ISteamUserStats/GetSchemaForGame/v2/?key={_apiKey}&appid={appId}&l=english", cancellationToken);

        var achievements = envelope.Game?.Stats?.Achievements ?? [];

        // Schema order is stable and reflects the order achievements were added,
        // which is the raw material for future DLC grouping. Preserve it.
        return achievements
            .Select((a, index) => new AchievementSchema(
                a.Name ?? string.Empty,
                a.DisplayName ?? string.Empty,
                a.Description ?? string.Empty,
                a.Icon ?? string.Empty,
                a.IconGray ?? string.Empty,
                a.Hidden == 1,
                index))
            .ToList();
    }

    public async Task<IReadOnlyList<PlayerAchievement>> GetPlayerAchievementsAsync(
        ulong steamId, uint appId, CancellationToken cancellationToken)
    {
        var envelope = await GetJsonAsync<PlayerStatsEnvelope>(
            $"ISteamUserStats/GetPlayerAchievements/v1/?key={_apiKey}&steamid={steamId}&appid={appId}&l=english",
            cancellationToken);

        return envelope.PlayerStats?.Achievements?
            .Select(a => new PlayerAchievement(
                a.ApiName ?? string.Empty,
                a.Achieved == 1,
                a.Achieved == 1 && a.UnlockTime > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(a.UnlockTime)
                    : null))
            .ToList()
            ?? [];
    }

    /// <summary>
    /// Global achievement rarity. This endpoint takes <c>gameid</c> rather than
    /// <c>appid</c> and requires no API key — see docs/steam-api.md.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, double>> GetGlobalPercentagesAsync(
        uint appId, CancellationToken cancellationToken)
    {
        var envelope = await GetJsonAsync<GlobalPercentagesEnvelope>(
            $"ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={appId}", cancellationToken);

        return envelope.Percentages?.Achievements?
            .Where(a => a.Name is not null)
            .ToDictionary(a => a.Name!, a => a.Percent)
            ?? new Dictionary<string, double>();
    }

    internal async Task<T> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(path, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller cancelled — this is not a transport failure, so let it
            // propagate as-is. The sync layer relies on cancellation working.
            throw;
        }
        catch (HttpRequestException)
        {
            // Connection refused, DNS failure, TLS error, etc. Never echo the
            // request URL here — it carries the API key.
            throw new SteamApiException(SteamApiErrorKind.ServerError, 0,
                "A network error occurred while contacting Steam.");
        }
        catch (TaskCanceledException)
        {
            // Not the caller's token, so this is HttpClient's own request
            // timeout — a transient condition, not a caller cancellation.
            throw new SteamApiException(SteamApiErrorKind.ServerError, 0,
                "The request to Steam timed out.");
        }

        using (response)
        {
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
    }

    private static SteamApiException Classify(HttpStatusCode status, string body)
    {
        // Bodies are HTML, not JSON — match on text, and never echo the body
        // back, because the request URL it may contain carries the API key.
        // Steam only emits "has no stats" on a 400; scoping the check avoids a
        // body-text coincidence on another status (e.g. a 429 whose body
        // happens to contain the phrase) downgrading a real failure into a
        // no-op.
        if (status == HttpStatusCode.BadRequest && body.Contains("has no stats", StringComparison.OrdinalIgnoreCase))
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
