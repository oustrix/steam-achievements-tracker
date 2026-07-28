namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// Where the log goes and how much of it is kept.
///
/// <paramref name="MaxFiles"/> counts the current file, so the default of four
/// means <c>log.txt</c> plus <c>log.1.txt</c> through <c>log.3.txt</c> — eight
/// megabytes in total, which is several full syncs of history and still small
/// enough to attach to an issue.
/// </summary>
public sealed record LogFileOptions(
    string Directory,
    string FileName = "log.txt",
    long MaxBytes = 2 * 1024 * 1024,
    int MaxFiles = 4);
