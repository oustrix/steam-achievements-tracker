namespace SteamAchievements.Core.App;

/// <summary>
/// Where the application keeps its data. Everything except asking the operating
/// system for the base directory happens here, so the only part that cannot run
/// under <c>dotnet test</c> is a single call to
/// <c>Environment.GetFolderPath</c> in the host.
///
/// The base directory is <c>%LOCALAPPDATA%</c> in production. That is
/// per-Windows-user, which matches the DPAPI <c>CurrentUser</c> scope the key is
/// stored under: two Windows users get two databases and two keys instead of a
/// shared library nobody but its owner can sync.
/// </summary>
public sealed record DataPaths(string Folder, string DatabaseFile, string SecretFile, string LogFile)
{
    public const string FolderName = "SteamAchievementsTracker";
    public const string DatabaseFileName = "library.db";
    public const string SecretFileName = "apikey.bin";
    public const string LogFileName = "log.txt";

    public static DataPaths Resolve(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new ArgumentException("A base directory is required.", nameof(baseDirectory));
        }

        var folder = Path.Combine(baseDirectory, FolderName);

        return new DataPaths(
            folder,
            Path.Combine(folder, DatabaseFileName),
            Path.Combine(folder, SecretFileName),
            Path.Combine(folder, LogFileName));
    }

    /// <summary>
    /// Separate from <see cref="Resolve"/> so that resolving stays a pure
    /// function and the one call that touches the disk is visible at the call
    /// site. <c>CreateDirectory</c> is already idempotent.
    /// </summary>
    public void EnsureFolderExists() => Directory.CreateDirectory(Folder);
}
