namespace SteamAchievements.Core.Presentation;

/// <summary>
/// One row of the completion queue, with every displayed value already
/// resolved to a string. The bar width is deliberately absent: it depends on
/// the largest effort in the currently visible list, which changes with every
/// filter keystroke and is therefore computed by the screen, not here.
/// </summary>
public sealed record QueueRow(
    uint   AppId,
    string Name,
    int    Unlocked,
    int    Total,
    int    CompletionPercent,
    double Effort,
    string EffortText,
    string EffortLabel,
    string Reason,
    int    PlaytimeHours,
    bool   Complete,
    bool   RarityUnknown);

/// <summary>
/// <paramref name="TotalGames"/> counts the whole library, including games
/// with no achievements at all, because that is the denominator the mockup
/// shows: "12 of 1 482 games".
/// </summary>
public sealed record QueueView(IReadOnlyList<QueueRow> Rows, int TotalGames);

public sealed record LibrarySummary(
    int    GameCount,
    int    AchievementCount,
    string CountsText,
    string LastSyncText);
