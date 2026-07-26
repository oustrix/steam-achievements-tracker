namespace SteamAchievements.Core.Presentation;

public enum QueueSort
{
    Effort,
    Completion,
    Playtime,
}

public sealed record QueueCriteria(
    QueueSort Sort,
    bool      Descending,
    string    Query,
    int       MinPlaytimeHours,
    bool      HideComplete)
{
    public static QueueCriteria Default { get; } =
        new(QueueSort.Effort, Descending: false, Query: "", MinPlaytimeHours: 0, HideComplete: true);
}

/// <summary>
/// Filtering and sorting for the completion queue. Pure and in Core rather
/// than in the component, because it is real behaviour with real edge cases
/// and belongs under ordinary unit tests.
/// </summary>
public static class QueueFilter
{
    public static IReadOnlyList<QueueRow> Apply(IReadOnlyList<QueueRow> rows, QueueCriteria criteria)
    {
        var query = criteria.Query.Trim();

        var filtered = rows.Where(r =>
            (!criteria.HideComplete || !r.Complete) &&
            r.PlaytimeHours >= criteria.MinPlaytimeHours &&
            (query.Length == 0 || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)));

        Func<QueueRow, double> key = criteria.Sort switch
        {
            QueueSort.Completion => r => r.CompletionPercent,
            QueueSort.Playtime => r => r.PlaytimeHours,
            _ => r => r.Effort,
        };

        // A stable secondary key keeps rows from swapping places between
        // renders when several games share an effort of exactly zero.
        //
        // Note this is OrderByDescending rather than OrderBy().Reverse():
        // Reverse returns IEnumerable, which has no ThenBy, so the secondary
        // key could not be applied after it.
        var ordered = criteria.Descending
            ? filtered.OrderByDescending(key).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            : filtered.OrderBy(key).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);

        return ordered.ToList();
    }

    /// <summary>
    /// Which direction a sort starts in when the user first picks it. Least
    /// work first is the entire point of the effort ranking; for completion
    /// and playtime the interesting end is the large one.
    /// </summary>
    public static bool DefaultDescending(QueueSort sort) => sort != QueueSort.Effort;
}
