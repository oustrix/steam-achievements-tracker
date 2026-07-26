using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.App;

/// <summary>
/// Turns "412 of 1482 games, started 90 seconds ago" into the two strings the
/// progress card renders. Pure, so the arithmetic and every degenerate case —
/// no elapsed time, nothing completed, a total of zero — are covered by tests
/// rather than by watching a real sync for nine minutes.
/// </summary>
public static class SyncProgressReport
{
    /// <summary>
    /// Empty until there is enough to divide by. A rate computed from the first
    /// completed game over a few milliseconds is a wild number, and showing a
    /// wild number is worse than showing none.
    /// </summary>
    public static string Rate(int completed, TimeSpan elapsed)
    {
        if (completed <= 0 || elapsed <= TimeSpan.Zero)
        {
            return string.Empty;
        }

        return $"{completed / elapsed.TotalSeconds:0.0} games/s";
    }

    /// <summary>
    /// Extrapolates from the average so far. Deliberately not a smarter
    /// estimate: the sync's own rate limiter dominates, so the average is
    /// already close, and anything cleverer would be untestable guesswork.
    /// </summary>
    public static string Eta(int completed, int total, TimeSpan elapsed)
    {
        if (completed <= 0 || total <= completed || elapsed <= TimeSpan.Zero)
        {
            return string.Empty;
        }

        var perGame = elapsed.TotalSeconds / completed;
        var remaining = TimeSpan.FromSeconds(perGame * (total - completed));

        if (remaining.TotalMinutes < 1)
        {
            return "~1 min left";
        }

        return remaining.TotalMinutes < 60
            ? $"~{(int)Math.Round(remaining.TotalMinutes)} min left"
            : $"~{remaining.TotalHours:0.#} h left";
    }
}
