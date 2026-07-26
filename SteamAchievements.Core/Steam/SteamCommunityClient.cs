using System.Xml.Linq;

namespace SteamAchievements.Core.Steam;

public sealed record PublicProfile(ulong SteamId64, string PersonaName, string AvatarUrl);

/// <summary>
/// The community site's public profile document. A separate class from
/// <see cref="SteamApiClient"/> on purpose: a different host, XML instead of
/// JSON, different error semantics and no API key. It does not belong next to
/// that client's <c>GetJsonAsync</c>.
///
/// Expects an <see cref="HttpClient"/> whose base address is
/// <c>https://steamcommunity.com/</c>.
/// </summary>
public sealed class SteamCommunityClient
{
    private readonly HttpClient _http;

    public SteamCommunityClient(HttpClient http) => _http = http;

    /// <summary>
    /// Null whenever the profile cannot be read, for any reason. This is used
    /// only to put a name and a picture on the "is this you?" step, so it must
    /// never block onboarding and never throws for a failure of its own — only
    /// for caller cancellation.
    /// </summary>
    public async Task<PublicProfile?> GetProfileAsync(ulong steamId64, CancellationToken cancellationToken)
    {
        string body;

        try
        {
            using var response = await _http.GetAsync($"profiles/{steamId64}/?xml=1", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return null;
        }

        XDocument document;

        try
        {
            document = XDocument.Parse(body);
        }
        catch (System.Xml.XmlException)
        {
            // An anonymous request for a private area is answered with the
            // login page, which HttpClient follows the redirect to and hands
            // back as HTML.
            return null;
        }

        // Verified 2026-07-26: a profile that does not exist answers HTTP 200
        // with <response><error>...</error></response>. The status code carries
        // no information, so the root element is the only reliable signal.
        var profile = document.Root;

        if (profile is null || profile.Name.LocalName != "profile")
        {
            return null;
        }

        return new PublicProfile(
            steamId64,
            (string?)profile.Element("steamID") ?? string.Empty,
            (string?)profile.Element("avatarFull") ?? string.Empty);
    }
}
