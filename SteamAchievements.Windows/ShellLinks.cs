using System.ComponentModel;
using System.Diagnostics;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Windows;

public sealed class ShellLinks : IExternalLinks
{
    public const string ApiKeyPage = "https://steamcommunity.com/dev/apikey";

    private readonly string _dataFolder;

    public ShellLinks(string dataFolder) => _dataFolder = dataFolder;

    public void OpenApiKeyPage() => OpenUrl(ApiKeyPage);

    public void OpenDataFolder() => OpenUrl(_dataFolder);

    public void OpenUrl(string url)
    {
        try
        {
            // UseShellExecute defaults to false on .NET, where a URL is treated
            // as an executable path and throws Win32Exception.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or ObjectDisposedException)
        {
            // No browser, no file association, or a folder that no longer
            // exists. Nothing useful to do, and nothing worth taking the window
            // down for.
        }
    }
}
