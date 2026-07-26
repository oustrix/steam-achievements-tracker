namespace SteamAchievements.Core.Data;

public sealed record StoredAchievement(
    string ApiName,
    string DisplayName,
    string Description,
    string IconUrl,
    string IconGrayUrl,
    bool IsHidden,
    int SortOrder,
    DateTimeOffset FirstSeenAt);

public sealed record GameSyncState(
    uint AppId,
    bool HasAchievements,
    int SyncedPlaytime,
    DateTimeOffset? SchemaSyncedAt,
    DateTimeOffset? GlobalSyncedAt,
    DateTimeOffset? PlayerSyncedAt);

public sealed record AchievementProgress(
    string ApiName,
    string DisplayName,
    string Description,
    string IconUrl,
    bool IsHidden,
    bool Unlocked,
    DateTimeOffset? UnlockedAt,
    double? GlobalPercent);
