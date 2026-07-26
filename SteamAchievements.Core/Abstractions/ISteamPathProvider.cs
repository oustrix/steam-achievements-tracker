namespace SteamAchievements.Core.Abstractions;

/// <summary>
/// Locates the local Steam installation. Implemented on Windows by reading
/// HKCU\Software\Valve\Steam\SteamPath; kept behind an interface so Core
/// stays free of Windows APIs and testable on any platform.
/// </summary>
public interface ISteamPathProvider
{
    string? FindSteamPath();
}
