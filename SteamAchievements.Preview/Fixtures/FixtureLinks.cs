using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>
/// Nothing leaves the preview host. The last URL is kept so a screen can show
/// what it would have opened — on macOS there is no Steam client to open it in
/// anyway.
/// </summary>
public sealed class FixtureLinks : IExternalLinks
{
    public string? LastUrl { get; private set; }

    public void OpenApiKeyPage() => OpenUrl("https://steamcommunity.com/dev/apikey");

    public void OpenDataFolder() => OpenUrl("(the data folder)");

    public void OpenUrl(string url) => LastUrl = url;
}
