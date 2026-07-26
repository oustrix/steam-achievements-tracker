using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests.Presentation;

public class QueueFilterTests
{
    private static QueueRow Row(string name, double effort, int unlocked, int total, int hours,
        bool complete = false) =>
        new(1, name, unlocked, total, 100 * unlocked / total, effort,
            effort.ToString("0.#"), "an evening", "reason", hours, complete, false);

    private static readonly IReadOnlyList<QueueRow> Library =
    [
        Row("Hollow Knight", 4.2, 60, 63, 84),
        Row("Stellaris", 188.5, 37, 219, 410),
        Row("Cuphead", 11.1, 33, 40, 27),
        Row("Celeste", 0, 45, 45, 68, complete: true),
    ];

    private static QueueCriteria Criteria => new(QueueSort.Effort, false, "", 0, HideComplete: false);

    [Fact]
    public void SortsByEffortAscendingSoTheLeastWorkComesFirst()
    {
        var result = QueueFilter.Apply(Library, Criteria);

        Assert.Equal(["Celeste", "Hollow Knight", "Cuphead", "Stellaris"], result.Select(r => r.Name));
    }

    [Fact]
    public void ReversesTheOrderWhenDescending()
    {
        var result = QueueFilter.Apply(Library, Criteria with { Descending = true });

        Assert.Equal("Stellaris", result[0].Name);
    }

    [Fact]
    public void SortsByCompletionAndByPlaytime()
    {
        var byCompletion = QueueFilter.Apply(Library,
            Criteria with { Sort = QueueSort.Completion, Descending = true });
        Assert.Equal("Celeste", byCompletion[0].Name);

        var byPlaytime = QueueFilter.Apply(Library,
            Criteria with { Sort = QueueSort.Playtime, Descending = true });
        Assert.Equal("Stellaris", byPlaytime[0].Name);
    }

    [Fact]
    public void HidesCompletedGamesWhenAsked()
    {
        var result = QueueFilter.Apply(Library, Criteria with { HideComplete = true });

        Assert.DoesNotContain(result, r => r.Name == "Celeste");
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void MatchesTitlesCaseInsensitivelyAndAnywhereInTheName()
    {
        var result = QueueFilter.Apply(Library, Criteria with { Query = "knight" });

        Assert.Equal("Hollow Knight", result.Single().Name);
    }

    [Fact]
    public void IgnoresSurroundingWhitespaceInTheSearchQuery()
    {
        Assert.Single(QueueFilter.Apply(Library, Criteria with { Query = "  cuphead " }));
    }

    [Fact]
    public void DropsGamesBelowTheMinimumPlaytime()
    {
        var result = QueueFilter.Apply(Library, Criteria with { MinPlaytimeHours = 68 });

        Assert.Equal(["Celeste", "Hollow Knight", "Stellaris"], result.Select(r => r.Name).Order());
    }

    [Fact]
    public void ReturnsAnEmptyListRatherThanThrowingWhenNothingMatches()
    {
        Assert.Empty(QueueFilter.Apply(Library, Criteria with { Query = "nothing here" }));
    }

    [Fact]
    public void DefaultsEffortToAscendingAndTheOtherTwoToDescending()
    {
        // Least work first is the point of the screen; for completion and
        // playtime the interesting end is the large one.
        Assert.False(QueueFilter.DefaultDescending(QueueSort.Effort));
        Assert.True(QueueFilter.DefaultDescending(QueueSort.Completion));
        Assert.True(QueueFilter.DefaultDescending(QueueSort.Playtime));
    }
}
