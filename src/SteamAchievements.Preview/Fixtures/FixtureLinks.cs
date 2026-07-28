using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>
/// Nothing leaves the preview host. The last URL is kept and shown — by
/// <c>LastLinkStrip</c>, which subscribes to <see cref="Opened"/> — so a button
/// that would launch a browser on Windows still says what it did on macOS,
/// where there is no Steam client to open it in.
/// </summary>
public sealed class FixtureLinks : IExternalLinks
{
    public string? LastUrl { get; private set; }

    /// <summary>Raised on every open, including a repeat of the same URL.</summary>
    public event Action? Opened;

    public void OpenApiKeyPage() => OpenUrl("https://steamcommunity.com/dev/apikey");

    public void OpenDataFolder() => OpenUrl("(the data folder)");

    public void OpenLogFile() => OpenUrl("(the log file)");

    public void OpenUrl(string url)
    {
        LastUrl = url;
        Opened?.Invoke();
    }
}
