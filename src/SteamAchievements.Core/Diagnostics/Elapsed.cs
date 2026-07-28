using System.Diagnostics;

namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// <c>(long)Stopwatch.GetElapsedTime(x).TotalMilliseconds</c> — every timed log
/// line in this project narrows the same way, because a structured field like
/// <c>{Elapsed}ms</c> reads as a whole number, not as a <c>double</c> carrying
/// nine decimal places nobody asked for. One place for the cast means one
/// place to change it, and every duration in the log file stays comparable.
/// </summary>
public static class Elapsed
{
    /// <param name="startTimestamp">A value from <see cref="Stopwatch.GetTimestamp"/>.</param>
    public static long Since(long startTimestamp) =>
        (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
}
