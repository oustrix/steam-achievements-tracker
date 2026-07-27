namespace SteamAchievements.Core.Presentation;

/// <summary>
/// Everything the screens know about stored data. Read-only by design — the
/// one write the UI makes goes through <see cref="IUserPreferences"/>.
///
/// <paramref name="now"/> is a parameter on every method rather than a clock
/// read inside, so the relative dates these views carry stay testable.
/// </summary>
public interface ILibraryQuery
{
    /// <summary>
    /// Every owned game that has achievements, unfiltered and unsorted.
    /// Filtering, sorting and search happen in the screen over this list:
    /// 1500 small records cost nothing to hold, and a round trip to SQLite on
    /// every keystroke would be slower and more code.
    /// </summary>
    QueueView GetQueue(DateTimeOffset now);

    /// <summary>Null when the app id is not in the library.</summary>
    GameDetailView? GetGame(uint appId, DateTimeOffset now);

    LibrarySummary GetSummary(DateTimeOffset now);

    IReadOnlyList<SyncRunView> GetSyncHistory(int limit, DateTimeOffset now);
}
