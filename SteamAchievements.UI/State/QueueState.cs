using SteamAchievements.Core.Presentation;

namespace SteamAchievements.UI.State;

/// <summary>
/// Sort, filters and selection for the completion queue, held outside the
/// component so they survive a drill-down into a game and back. The mockup is
/// one application with one state: select a row, press Enter, return, and the
/// selection and sort are still there.
/// </summary>
public sealed class QueueState
{
    public QueueCriteria Criteria { get; private set; } = QueueCriteria.Default;
    public uint? SelectedAppId { get; private set; }

    /// <summary>
    /// Sort, search, minimum playtime or hide-complete changed, so the visible
    /// list has to be filtered and sorted again.
    /// </summary>
    public event Action? CriteriaChanged;

    /// <summary>
    /// Only the selected row changed. Deliberately a separate event: hovering
    /// the queue selects a row per pointer crossing and every arrow keypress
    /// moves the selection, and re-running the filter over the whole library
    /// for that would cost a Where/OrderBy/ToList over ~1500 rows each time.
    /// Selection changes nothing about which rows are visible — a re-render is
    /// the whole response.
    /// </summary>
    public event Action? SelectionChanged;

    /// <summary>Picking the active sort again flips its direction, as in the mockup.</summary>
    public void SortBy(QueueSort sort) => Update(Criteria with
    {
        Sort = sort,
        Descending = Criteria.Sort == sort
            ? !Criteria.Descending
            : QueueFilter.DefaultDescending(sort),
    });

    public void SetQuery(string query) => Update(Criteria with { Query = query });

    public void SetMinPlaytime(int hours) => Update(Criteria with { MinPlaytimeHours = hours });

    public void ToggleComplete() => Update(Criteria with { HideComplete = !Criteria.HideComplete });

    public void Select(uint appId)
    {
        if (SelectedAppId == appId)
        {
            return;
        }

        SelectedAppId = appId;
        SelectionChanged?.Invoke();
    }

    private void Update(QueueCriteria criteria)
    {
        Criteria = criteria;
        CriteriaChanged?.Invoke();
    }
}
