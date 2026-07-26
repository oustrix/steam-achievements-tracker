using Microsoft.Win32;
using SteamAchievements.Core.Abstractions;

namespace SteamAchievements.Windows;

public sealed class RegistrySteamPathProvider : ISteamPathProvider
{
    public string? FindSteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var path = key?.GetValue("SteamPath") as string;

        // Steam writes this value with forward slashes, which Path.Combine will
        // happily mix with backslashes into something Windows still opens but
        // nobody wants to read in a log.
        return string.IsNullOrWhiteSpace(path) ? null : path.Replace('/', '\\');
    }
}
