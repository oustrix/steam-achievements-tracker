using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Steam;

/// <summary>
/// The one place that builds a <see cref="SteamApiClient"/> around a
/// <see cref="FakeHttpMessageHandler"/>. Returns the handler alongside the
/// client so callers that need to inspect the outgoing request (headers,
/// query string, request count) don't have to duplicate the construction
/// just to keep a reference to it.
/// </summary>
internal static class TestSteamApiClientFactory
{
    public const string ApiKey = "TESTKEY";

    public static (SteamApiClient Client, FakeHttpMessageHandler Handler) Create(
        FakeHttpMessageHandler handler, string apiKey = ApiKey) =>
        (new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, apiKey),
            handler);
}
