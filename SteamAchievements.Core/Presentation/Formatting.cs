using System.Globalization;

namespace SteamAchievements.Core.Presentation;

/// <summary>
/// Display strings shared by every screen.
///
/// Pure by construction: <c>now</c> is always a parameter and never read from
/// the system clock, which is what keeps relative dates testable without
/// freezing time or injecting a clock abstraction.
/// </summary>
public static class Formatting
{
    private static readonly string[] Words =
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];

    /// <summary>
    /// Counts appearing inside a sentence: words up to nine, digits from ten.
    /// The leading count of a sentence is not this — see <see cref="Number"/>.
    /// </summary>
    public static string Count(int value) =>
        value >= 0 && value < Words.Length ? Words[value] : Number(value);

    /// <summary>Thousands separated by a thin space, as in the mockup: 1 482.</summary>
    public static string Number(long value) =>
        value.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    public static string Percent(double value) =>
        value.ToString("0.0", CultureInfo.InvariantCulture) + "%";

    public static string Playtime(int minutes) =>
        minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h";

    public static string Date(DateTimeOffset value) =>
        value.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// "14 min ago", "3 days ago", "a year ago". The buckets are coarse on
    /// purpose: the exact age of a sync or a last-played date is never the
    /// point, only its rough distance.
    /// </summary>
    public static string Relative(DateTimeOffset value, DateTimeOffset now)
    {
        var span = now - value;

        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} h ago";

        var days = (int)span.TotalDays;

        if (days == 1) return "yesterday";
        if (days < 7) return $"{days} days ago";
        if (days < 14) return "a week ago";
        if (days < 28) return $"{days / 7} weeks ago";
        if (days < 60) return "a month ago";
        if (days < 365) return $"{days / 30} months ago";
        if (days < 730) return "a year ago";

        return $"{days / 365} years ago";
    }

    /// <summary>Sync history: clock time while it is still recent, a date after that.</summary>
    public static string Timestamp(DateTimeOffset value, DateTimeOffset now)
    {
        var time = value.ToString("HH:mm", CultureInfo.InvariantCulture);
        var days = (now.Date - value.Date).Days;

        return days switch
        {
            0 => $"today {time}",
            1 => $"yesterday {time}",
            _ => $"{value.ToString("d MMM", CultureInfo.InvariantCulture)} {time}",
        };
    }

    /// <summary>
    /// Seconds with one decimal below a minute, then minutes and zero-padded
    /// seconds — "2.1 s", "8 min 51 s". Matches the mockup's history rows.
    /// </summary>
    public static string Duration(long milliseconds)
    {
        if (milliseconds < 60_000)
        {
            return (milliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s";
        }

        var total = milliseconds / 1000;
        return $"{total / 60} min {total % 60:00} s";
    }
}
