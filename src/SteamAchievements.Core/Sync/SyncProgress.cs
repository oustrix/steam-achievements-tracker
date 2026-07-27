namespace SteamAchievements.Core.Sync;

public sealed record SyncProgress(int Completed, int Total, string CurrentGame);
