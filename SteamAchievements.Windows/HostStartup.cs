namespace SteamAchievements.Windows;

/// <summary>
/// What the window needs to know before it decides what to draw.
/// <paramref name="FailureMessage"/> is non-null when composition itself failed
/// — a locked or corrupt database — in which case there is no service provider
/// and the window shows the message instead of a WebView.
/// </summary>
public sealed record HostStartup(
    IServiceProvider? Services,
    string StartPath,
    string? FailureMessage,
    string DataFolder);
