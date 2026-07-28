namespace SteamAchievements.Core.Presentation;

/// <summary>
/// Anything that leaves the application window. Implemented in the WPF project
/// with <c>Process.Start</c>; declared here so the screens do not reference it.
/// </summary>
public interface IExternalLinks
{
    /// <summary>Steam's API key issuance page.</summary>
    void OpenApiKeyPage();

    /// <summary>The folder holding the database and the stored key.</summary>
    void OpenDataFolder();

    /// <summary>The log file, for a user who is reporting a problem.</summary>
    void OpenLogFile();

    void OpenUrl(string url);
}
