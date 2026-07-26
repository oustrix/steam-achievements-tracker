namespace SteamAchievements.Core.Steam;

public enum SteamApiErrorKind
{
    /// <summary>Key missing, malformed or rejected. Retrying will not help.</summary>
    InvalidKey,

    /// <summary>The app has no achievements at all. Expected for 30-40% of a library.</summary>
    NoStatsForApp,

    RateLimited,
    ServerError,
    BadRequest,
    Unknown,
}
