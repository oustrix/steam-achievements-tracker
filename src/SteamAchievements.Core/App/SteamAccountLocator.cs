using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.Local;

namespace SteamAchievements.Core.App;

/// <summary>
/// Turns "where is Steam" into "who is signed in". Reading a file is not a
/// Windows API, so all of this stays testable on macOS with a fake path
/// provider; only <see cref="ISteamPathProvider"/> itself needs the registry.
/// </summary>
public sealed class SteamAccountLocator
{
    private readonly ISteamPathProvider _paths;

    public SteamAccountLocator(ISteamPathProvider paths) => _paths = paths;

    public static string LoginUsersPath(string steamPath) =>
        Path.Combine(steamPath, "config", "loginusers.vdf");

    /// <summary>
    /// Empty whenever the answer cannot be had — Steam is not installed, the
    /// file is missing, unreadable, or malformed. None of those is exceptional:
    /// they all lead to the same screen, where the user types a SteamID by hand.
    /// </summary>
    public IReadOnlyList<SteamAccount> FindAccounts()
    {
        var steamPath = _paths.FindSteamPath();

        if (string.IsNullOrWhiteSpace(steamPath))
        {
            return [];
        }

        var file = LoginUsersPath(steamPath);

        if (!File.Exists(file))
        {
            return [];
        }

        try
        {
            return LoginUsersReader.Read(File.ReadAllText(file));
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            // VdfParser throws FormatException on unbalanced braces and
            // unterminated strings — a half-written file during a Steam update
            // looks exactly like that.
            return [];
        }
    }

    public SteamAccount? FindActiveAccount() => LoginUsersReader.SelectActive(FindAccounts());
}
