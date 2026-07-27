namespace SteamAchievements.Core.Sync;

public sealed record SyncOptions(TimeSpan SchemaTtl, TimeSpan GlobalTtl)
{
    public static SyncOptions Default { get; } = new(TimeSpan.FromDays(30), TimeSpan.FromDays(7));
}
