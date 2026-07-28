namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// Where the log goes and how much of it is kept.
///
/// <paramref name="MaxFiles"/> counts the current file, so the default of four
/// means <c>log.txt</c> plus <c>log.1.txt</c> through <c>log.3.txt</c> — eight
/// megabytes in total, which is several full syncs of history and still small
/// enough to attach to an issue.
///
/// Validated at construction rather than trusted, because a bad value here
/// does not fail loudly on its own: <c>RollingFileWriter.Rotate</c> can only
/// express two or more rotated files, and a smaller value degrades into a
/// permanently disabled writer on the *second* rotation of a long-running
/// sync — see <see cref="RollingFileWriter"/> for why. Failing here instead
/// means a bad value is caught at startup, on whatever machine it was typed
/// on, not discovered mid-sync after the fact.
/// </summary>
public sealed record LogFileOptions(
    string Directory,
    string FileName = "log.txt",
    long MaxBytes = 2 * 1024 * 1024,
    int MaxFiles = 4)
{
    /// <summary>
    /// Mirrors the wording and exception type <c>DataPaths.Resolve</c> uses for
    /// the same complaint about a base directory.
    /// </summary>
    public string Directory { get; init; } = string.IsNullOrWhiteSpace(Directory)
        ? throw new ArgumentException("A directory is required.", nameof(Directory))
        : Directory;

    public long MaxBytes { get; init; } = MaxBytes > 0
        ? MaxBytes
        : throw new ArgumentOutOfRangeException(
            nameof(MaxBytes), MaxBytes, "MaxBytes must be positive.");

    /// <summary>
    /// Two is the floor, not one: <c>RollingFileWriter.Rotate</c> shifts
    /// <c>log.txt</c> into <c>log.1.txt</c>, which only stays valid across a
    /// second rotation if there is a <c>log.1.txt</c> slot to shift *out of*
    /// first. <c>MaxFiles = 1</c> never creates the <c>log.0.txt</c> that
    /// would let the delete step clear that slot, so the second rotation's
    /// <c>File.Move</c> collides with a destination that already exists.
    /// </summary>
    public int MaxFiles { get; init; } = MaxFiles >= 2
        ? MaxFiles
        : throw new ArgumentOutOfRangeException(
            nameof(MaxFiles), MaxFiles, "MaxFiles must be at least 2.");
}
