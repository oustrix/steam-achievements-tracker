namespace SteamAchievements.Core.Local;

public sealed record SteamAccount(
    ulong SteamId64,
    string AccountName,
    string PersonaName,
    bool MostRecent,
    DateTimeOffset Timestamp);
