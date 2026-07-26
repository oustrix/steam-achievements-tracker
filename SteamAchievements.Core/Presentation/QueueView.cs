namespace SteamAchievements.Core.Presentation;

/// <summary>
/// One row of the completion queue, with every displayed value already
/// resolved to a string. The bar width is deliberately absent: it depends on
/// the largest effort in the currently visible list, which changes with every
/// filter keystroke and is therefore computed by the screen, not here.
/// </summary>
public sealed record QueueRow(
    uint AppId,
    string Name,
    int Unlocked,
    int Total,
    int CompletionPercent,
    double Effort,
    string EffortText,
    string EffortLabel,
    string Reason,
    int PlaytimeHours,
    bool Complete,
    bool RarityUnknown);

/// <summary>
/// <paramref name="TotalGames"/> counts the whole library, including games
/// with no achievements at all, because that is the denominator the mockup
/// shows: "12 of 1 482 games".
/// </summary>
public sealed record QueueView(IReadOnlyList<QueueRow> Rows, int TotalGames);

public sealed record LibrarySummary(
    int GameCount,
    int AchievementCount,
    string CountsText,
    string LastSyncText)
{
    /// <summary>
    /// Builds the sidebar summary from the two counts and the last sync time.
    ///
    /// Composed here rather than at each call site because the preview host
    /// renders the same card from fixtures; two hand-written copies of the same
    /// sentence let the preview drift away from the real screen.
    /// </summary>
    public static LibrarySummary Build(
        int games, int achievements, DateTimeOffset? lastSync, DateTimeOffset now) =>
        new(games,
            achievements,
            $"{Formatting.Number(games)} games · {Formatting.Number(achievements)} ach.",
            lastSync is { } at ? $"Last sync {Formatting.Relative(at, now)}" : "Never synced");
}
