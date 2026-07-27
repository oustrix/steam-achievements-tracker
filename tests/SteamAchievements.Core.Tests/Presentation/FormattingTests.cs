using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests.Presentation;

public class FormattingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SpellsCountsUpToNineAndSwitchesToDigitsAtTen()
    {
        Assert.Equal("one", Formatting.Count(1));
        Assert.Equal("four", Formatting.Count(4));
        Assert.Equal("nine", Formatting.Count(9));
        Assert.Equal("10", Formatting.Count(10));
        Assert.Equal("41", Formatting.Count(41));
    }

    [Fact]
    public void SeparatesThousandsWithAThinSpaceAsInTheMockup()
    {
        Assert.Equal("1 482", Formatting.Number(1482));
        Assert.Equal("61 214", Formatting.Number(61214));
        Assert.Equal("3", Formatting.Number(3));
    }

    [Fact]
    public void ShowsPlaytimeInMinutesBelowAnHourAndInWholeHoursAbove()
    {
        Assert.Equal("48 min", Formatting.Playtime(48));
        Assert.Equal("1 h", Formatting.Playtime(60));
        Assert.Equal("84 h", Formatting.Playtime(5040));
    }

    [Fact]
    public void FormatsPercentagesWithASingleDecimal()
    {
        Assert.Equal("2.1%", Formatting.Percent(2.1));
        Assert.Equal("10.0%", Formatting.Percent(10));
        Assert.Equal("0.4%", Formatting.Percent(0.42));
    }

    [Fact]
    public void FormatsAbsoluteDatesTheWayTheMockupDoes()
    {
        Assert.Equal("24 Mar 2026", Formatting.Date(new DateTimeOffset(2026, 3, 24, 8, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(14, "14 min ago")]
    [InlineData(60 * 3, "3 h ago")]
    [InlineData(60 * 24, "yesterday")]
    [InlineData(60 * 24 * 3, "3 days ago")]
    [InlineData(60 * 24 * 8, "a week ago")]
    [InlineData(60 * 24 * 15, "2 weeks ago")]
    [InlineData(60 * 24 * 40, "a month ago")]
    [InlineData(60 * 24 * 120, "4 months ago")]
    [InlineData(60 * 24 * 400, "a year ago")]
    [InlineData(60 * 24 * 800, "2 years ago")]
    public void DescribesHowLongAgoSomethingHappened(int minutesAgo, string expected)
    {
        Assert.Equal(expected, Formatting.Relative(Now.AddMinutes(-minutesAgo), Now));
    }

    [Fact]
    public void TimestampsUseClockTimeForTodayAndYesterdayAndADateBeforeThat()
    {
        Assert.Equal("today 09:15", Formatting.Timestamp(
            new DateTimeOffset(2026, 7, 26, 9, 15, 0, TimeSpan.Zero), Now));
        Assert.Equal("yesterday 22:40", Formatting.Timestamp(
            new DateTimeOffset(2026, 7, 25, 22, 40, 0, TimeSpan.Zero), Now));
        Assert.Equal("22 Jul 09:15", Formatting.Timestamp(
            new DateTimeOffset(2026, 7, 22, 9, 15, 0, TimeSpan.Zero), Now));
    }

    [Fact]
    public void ShowsSubMinuteDurationsInSecondsAndLongerOnesInMinutes()
    {
        Assert.Equal("2.1 s", Formatting.Duration(2149));
        Assert.Equal("0.9 s", Formatting.Duration(910));
        Assert.Equal("8 min 51 s", Formatting.Duration(531_000));
        Assert.Equal("1 min 06 s", Formatting.Duration(66_000));
    }
}
