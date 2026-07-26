using System.Text.Json.Serialization;

namespace SteamAchievements.Core.Steam;

public sealed record OwnedGame(
    uint AppId,
    string Name,
    string IconHash,
    int PlaytimeForever,
    int PlaytimeTwoWeeks,
    DateTimeOffset? LastPlayed);

public sealed record AchievementSchema(
    string ApiName,
    string DisplayName,
    string Description,
    string IconUrl,
    string IconGrayUrl,
    bool IsHidden,
    int SortOrder);

public sealed record PlayerAchievement(
    string ApiName,
    bool Unlocked,
    DateTimeOffset? UnlockedAt);

// Wire shapes below mirror Steam's JSON exactly and stay internal.

internal sealed class OwnedGamesEnvelope
{
    [JsonPropertyName("response")] public OwnedGamesResponse? Response { get; set; }
}

internal sealed class OwnedGamesResponse
{
    [JsonPropertyName("games")] public List<OwnedGameDto>? Games { get; set; }
}

internal sealed class OwnedGameDto
{
    [JsonPropertyName("appid")] public uint AppId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("img_icon_url")] public string? IconHash { get; set; }
    [JsonPropertyName("playtime_forever")] public int PlaytimeForever { get; set; }
    [JsonPropertyName("playtime_2weeks")] public int PlaytimeTwoWeeks { get; set; }
    [JsonPropertyName("rtime_last_played")] public long LastPlayed { get; set; }
}

internal sealed class SchemaEnvelope
{
    [JsonPropertyName("game")] public SchemaGame? Game { get; set; }
}

internal sealed class SchemaGame
{
    [JsonPropertyName("availableGameStats")] public SchemaStats? Stats { get; set; }
}

internal sealed class SchemaStats
{
    [JsonPropertyName("achievements")] public List<SchemaAchievementDto>? Achievements { get; set; }
}

internal sealed class SchemaAchievementDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("hidden")] public int Hidden { get; set; }
    [JsonPropertyName("icon")] public string? Icon { get; set; }
    [JsonPropertyName("icongray")] public string? IconGray { get; set; }
}

internal sealed class PlayerStatsEnvelope
{
    [JsonPropertyName("playerstats")] public PlayerStats? PlayerStats { get; set; }
}

internal sealed class PlayerStats
{
    [JsonPropertyName("achievements")] public List<PlayerAchievementDto>? Achievements { get; set; }
}

internal sealed class PlayerAchievementDto
{
    [JsonPropertyName("apiname")] public string? ApiName { get; set; }
    [JsonPropertyName("achieved")] public int Achieved { get; set; }
    [JsonPropertyName("unlocktime")] public long UnlockTime { get; set; }
}

internal sealed class GlobalPercentagesEnvelope
{
    [JsonPropertyName("achievementpercentages")] public GlobalPercentages? Percentages { get; set; }
}

internal sealed class GlobalPercentages
{
    [JsonPropertyName("achievements")] public List<GlobalPercentageDto>? Achievements { get; set; }
}

internal sealed class GlobalPercentageDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("percent")] public double Percent { get; set; }
}
