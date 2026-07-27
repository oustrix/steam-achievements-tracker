using System.Text.RegularExpressions;

namespace SteamAchievements.Cli;

/// <summary>
/// Captures one raw JSON response body per Steam endpoint the sync touches,
/// so the synthetic test fixtures under tests/testdata
/// can eventually be replaced with real ones. A DelegatingHandler in the CLI
/// project rather than a change to SteamApiClient: Core has no reason to know
/// its responses might be dumped to disk, and the anonymization step below is
/// entirely a CLI/testdata concern.
///
/// Anonymization is defense in depth, not a single string.Replace: it strips
/// any literal API key or query-string "key=..." value, and replaces every
/// occurrence of the real SteamID64 with a fixed placeholder. As a last
/// resort, <see cref="Anonymize"/> throws rather than let a write proceed if
/// either secret is still present in the scrubbed text — a committed API key
/// is a leaked credential.
/// </summary>
public sealed class FixtureCapturingHandler(HttpMessageHandler inner, string outputDirectory, string apiKey, string realSteamId)
    : DelegatingHandler(inner)
{
    private const string PlaceholderSteamId = "76561190000000002";

    private static readonly Regex KeyQueryParam = new(@"key=[^&""\s]*", RegexOptions.Compiled);

    private static readonly (string PathFragment, string FileName)[] Endpoints =
    [
        ("/IPlayerService/GetOwnedGames/", "owned-games.json"),
        ("/ISteamUserStats/GetSchemaForGame/", "achievement-schema.json"),
        ("/ISteamUserStats/GetPlayerAchievements/", "player-achievements.json"),
        ("/ISteamUserStats/GetGlobalAchievementPercentagesForApp/", "global-percentages.json"),
    ];

    private readonly HashSet<string> _captured = [];
    private readonly Lock _lock = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode && response.Content is not null && request.RequestUri is not null)
        {
            var match = Endpoints.FirstOrDefault(e => request.RequestUri.AbsolutePath.Contains(e.PathFragment));
            if (match.FileName is not null)
            {
                // LoadIntoBufferAsync makes the content replayable: the caller
                // (SteamApiClient) still needs to read this same response body
                // after we return it.
                await response.Content.LoadIntoBufferAsync(cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                await TryCaptureAsync(match.FileName, body, cancellationToken);
            }
        }

        return response;
    }

    private async Task TryCaptureAsync(string fileName, string body, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_captured.Add(fileName))
            {
                return;
            }
        }

        var anonymized = Anonymize(body, apiKey, realSteamId);
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, fileName);
        await File.WriteAllTextAsync(path, anonymized, cancellationToken);
        Console.WriteLine($"Wrote fixture: {path}");
    }

    internal static string Anonymize(string body, string apiKey, string realSteamId)
    {
        var result = body;

        if (!string.IsNullOrEmpty(apiKey))
        {
            result = result.Replace(apiKey, "REDACTED");
        }

        result = KeyQueryParam.Replace(result, "key=REDACTED");

        if (!string.IsNullOrEmpty(realSteamId))
        {
            result = result.Replace(realSteamId, PlaceholderSteamId);
        }

        if (!string.IsNullOrEmpty(apiKey) && result.Contains(apiKey))
        {
            throw new InvalidOperationException("Refusing to write fixture: API key still present after scrubbing.");
        }

        if (!string.IsNullOrEmpty(realSteamId) && result.Contains(realSteamId))
        {
            throw new InvalidOperationException("Refusing to write fixture: real SteamID64 still present after scrubbing.");
        }

        return result;
    }
}
