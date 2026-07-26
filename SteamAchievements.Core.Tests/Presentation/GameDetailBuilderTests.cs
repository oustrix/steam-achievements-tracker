using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Presentation;

public class GameDetailBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    private static OwnedGame Game() =>
        new(367520, "Hollow Knight", "hash", 5040, 0, Now.AddDays(-3));

    private static AchievementProgress Achievement(
        string name, bool unlocked, double? percent,
        DateTimeOffset? at = null, bool hidden = false, string description = "Do the thing") =>
        new("api_" + name, name, description, "https://icon", hidden, unlocked, at, percent);

    [Fact]
    public void DescribesTheGameHeader()
    {
        var view = GameDetailBuilder.Build(Game(),
        [
            Achievement("A", unlocked: true, percent: 60, at: Now.AddDays(-10)),
            Achievement("B", unlocked: false, percent: 30),
        ], Now);

        Assert.Equal("Hollow Knight", view.Name);
        Assert.Equal("84 h", view.PlaytimeText);
        Assert.Equal("3 days ago", view.LastPlayedText);
        Assert.Equal(1, view.Unlocked);
        Assert.Equal(2, view.Total);
        Assert.Equal(50, view.CompletionPercent);
        Assert.Equal(1, view.Remaining);
        Assert.Equal("30.0%", view.RarestText);
    }

    [Fact]
    public void SaysNeverWhenTheGameWasNeverLaunched()
    {
        var game = new OwnedGame(367520, "Hollow Knight", "hash", 0, 0, null);

        var view = GameDetailBuilder.Build(game, [Achievement("A", false, 30)], Now);

        Assert.Equal("never", view.LastPlayedText);
    }

    [Fact]
    public void OrdersRemainingAchievementsCheapestFirst()
    {
        var view = GameDetailBuilder.Build(Game(),
        [
            Achievement("Rare", unlocked: false, percent: 2.1),
            Achievement("Cheap", unlocked: false, percent: 9.8),
            Achievement("Middle", unlocked: false, percent: 4.6),
        ], Now);

        Assert.Equal(["Cheap", "Middle", "Rare"], view.RemainingAchievements.Select(a => a.Name));
    }

    [Fact]
    public void OrdersUnlockedAchievementsMostRecentFirst()
    {
        var view = GameDetailBuilder.Build(Game(),
        [
            Achievement("Older", unlocked: true, percent: 28.9, at: Now.AddDays(-29)),
            Achievement("Newest", unlocked: true, percent: 3.4, at: Now.AddDays(-14)),
            Achievement("Middle", unlocked: true, percent: 11.2, at: Now.AddDays(-17)),
        ], Now);

        Assert.Equal(["Newest", "Middle", "Older"], view.UnlockedAchievements.Select(a => a.Name));
        Assert.Equal("12 Jul 2026", view.UnlockedAchievements[0].UnlockedDateText);
    }

    [Fact]
    public void ScalesTheRarityBarAgainstTheGamesMostCommonAchievement()
    {
        var view = GameDetailBuilder.Build(Game(),
        [
            Achievement("Common", unlocked: true, percent: 28.0),
            Achievement("Half", unlocked: false, percent: 14.0),
        ], Now);

        Assert.Equal(50, view.RemainingAchievements.Single().RarityBarPercent);
    }

    [Fact]
    public void ReportsUnknownRarityRatherThanDrawingAZeroLengthBar()
    {
        var view = GameDetailBuilder.Build(Game(),
        [
            Achievement("Common", unlocked: true, percent: 28.0),
            Achievement("Mystery", unlocked: false, percent: null),
        ], Now);

        var row = view.RemainingAchievements.Single();
        Assert.Equal("rarity unknown", row.PercentText);
        Assert.Equal(0, row.RarityBarPercent);
        Assert.Equal("1.0", row.CostText);
    }

    [Fact]
    public void SaysRarestIsUnknownWhenNothingLockedHasAPercentage()
    {
        var view = GameDetailBuilder.Build(Game(),
        [
            Achievement("Common", unlocked: true, percent: 28.0),
            Achievement("Mystery", unlocked: false, percent: null),
        ], Now);

        Assert.Equal("unknown", view.RarestText);
    }

    [Fact]
    public void ExplainsWhyAHiddenAchievementHasNoDescription()
    {
        var view = GameDetailBuilder.Build(Game(),
        [
            Achievement("Secret", unlocked: false, percent: 2.1, hidden: true, description: ""),
        ], Now);

        var row = view.RemainingAchievements.Single();
        Assert.True(row.Hidden);
        Assert.Equal("Steam returns no description for hidden achievements", row.Description);
    }

    [Fact]
    public void KeepsAHiddenAchievementsDescriptionWhenSteamActuallySuppliedOne()
    {
        var view = GameDetailBuilder.Build(Game(),
        [
            Achievement("Secret", unlocked: false, percent: 2.1, hidden: true, description: "Real text"),
        ], Now);

        Assert.Equal("Real text", view.RemainingAchievements.Single().Description);
    }

    [Fact]
    public void AgreesWithTheQueueOnEffortAndItsLabel()
    {
        var achievements = new[]
        {
            Achievement("Common", unlocked: true, percent: 80),
            Achievement("Locked", unlocked: false, percent: 20),
        };

        var view = GameDetailBuilder.Build(Game(), achievements, Now);
        var row = QueueRowBuilder.Build(Game(), achievements);

        Assert.Equal(row.EffortText, view.EffortText);
        Assert.Equal(row.EffortLabel, view.EffortLabel);
    }
}
