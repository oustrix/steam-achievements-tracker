using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Windows;

/// <summary>
/// What the window needs to know before it decides what to draw.
///
/// <paramref name="Services"/> is null when composition itself failed — a
/// locked or corrupt database — and <paramref name="FailureMessage"/> then says
/// why. <paramref name="Links"/> is built before composition and so survives
/// that failure, which is what lets the failure screen offer "open the data
/// folder" without rebuilding the opener by hand.
/// </summary>
public sealed record HostStartup(
    IServiceProvider? Services,
    string StartPath,
    string? FailureMessage,
    string DataFolder,
    IExternalLinks Links);
