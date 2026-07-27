using SteamAchievements.Core.Abstractions;

namespace SteamAchievements.Cli;

/// <summary>
/// Locates a local Steam install by checking well-known filesystem paths for
/// the current OS. Deliberately lives in the CLI project, not Core: the
/// production Windows host will instead read HKCU\Software\Valve\Steam\SteamPath
/// via the registry (see the comment on <see cref="ISteamPathProvider"/>), and
/// that implementation must never leak Microsoft.Win32 into Core. This
/// path-probing version is a reasonable stand-in for a tool that also needs
/// to run on macOS and Linux, where no registry exists.
/// </summary>
public sealed class CliSteamPathProvider : ISteamPathProvider
{
    public string? FindSteamPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");

            if (!string.IsNullOrEmpty(programFilesX86))
            {
                candidates.Add(Path.Combine(programFilesX86, "Steam"));
            }

            if (!string.IsNullOrEmpty(programFiles))
            {
                candidates.Add(Path.Combine(programFiles, "Steam"));
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            candidates.Add(Path.Combine(home, "Library", "Application Support", "Steam"));
        }
        else if (OperatingSystem.IsLinux())
        {
            candidates.Add(Path.Combine(home, ".steam", "steam"));
            candidates.Add(Path.Combine(home, ".local", "share", "Steam"));
        }

        return candidates.FirstOrDefault(Directory.Exists);
    }
}
