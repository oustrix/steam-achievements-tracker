namespace SteamAchievements.Core.Presentation;

/// <summary>
/// One achievement as the game screen shows it. Used for both the remaining
/// and the unlocked list; <paramref name="UnlockedDateText"/> is null for the
/// former.
/// </summary>
public sealed record AchievementRow(
    string  Name,
    string  Description,
    string  IconUrl,
    bool    Hidden,
    double? GlobalPercent,
    string  PercentText,
    int     RarityBarPercent,
    string  CostText,
    string? UnlockedDateText);

public sealed record GameDetailView(
    uint   AppId,
    string Name,
    string PlaytimeText,
    string LastPlayedText,
    int    Unlocked,
    int    Total,
    int    CompletionPercent,
    string EffortText,
    string EffortLabel,
    int    Remaining,
    string RarestText,
    IReadOnlyList<AchievementRow> RemainingAchievements,
    IReadOnlyList<AchievementRow> UnlockedAchievements);
