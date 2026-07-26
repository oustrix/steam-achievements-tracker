using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Preview.Fixtures;

public sealed record FixtureGame(OwnedGame Game, IReadOnlyList<AchievementProgress> Achievements);

/// <summary>
/// The mockup's library, reconstructed from achievement counts and rarity
/// rather than from its hand-written summary lines: the real ReasonWriter
/// then has to produce those lines itself, which is the point of looking at
/// the preview at all.
/// </summary>
public static class FixtureData
{
    /// <summary>Fixed so the preview never shifts under the tester's feet.</summary>
    public static readonly DateTimeOffset Now = new(2026, 7, 26, 14, 16, 0, TimeSpan.Zero);

    private static readonly Random Rng = new(20260726);

    public static IReadOnlyList<FixtureGame> All { get; } =
    [
        Build(367520, "Hollow Knight",            60,  63,  84 * 60,   3, [9.8, 4.6, 2.1]),
        Build(588650, "Dead Cells",               82,  88, 121 * 60,   1, [8.4, 9.1, 11.0, 12.5, 14.0, 20.2]),
        Build(391540, "Undertale",                46,  51,  39 * 60,  14, [3.7, 22.0, 24.0, 26.0, 30.0]),
        Build(268910, "Cuphead",                  33,  40,  27 * 60,  30, [1.4, 2.2, 3.0, 6.0, 9.0, 12.0, 18.0]),
        Build(413150, "Stardew Valley",           28,  39, 203 * 60,   5, [5.2, 6.0, 7.5, 9.0, 11.0, 13.0, 16.0, 19.0, 22.0, 25.0, 28.0]),
        Build(646570, "Slay the Spire",           34,  50, 156 * 60,   7, [1.9, 2.6, 3.4, 4.8, 6.0, 7.0, 8.0, 9.0, 11.0, 13.0, 15.0, 17.0, 19.0, 21.0, 24.0, 27.0]),
        Build(105600, "Terraria",                 63, 115, 312 * 60,  60, Spread(52, 0.9, 34.0)),
        Build(379720, "DOOM",                     29,  54,  44 * 60, 120, Spread(25, 1.1, 30.0)),
        Build(292030, "The Witcher 3: Wild Hunt", 41,  78, 187 * 60, 240, Spread(37, 3.1, 40.0)),
        BuildUnknown(435150, "Divinity: Original Sin 2", 12, 51, 62 * 60, 365, unknownCount: 6),
        Build(281990, "Stellaris",                37, 219, 410 * 60,  21, Spread(182, 0.4, 26.0)),
        Build(236850, "Europa Universalis IV",    19, 316, 289 * 60, 730, Spread(297, 0.2, 22.0)),
        Build(504230, "Celeste",                  45,  45,  68 * 60, 120, []),
        Build(620,    "Portal 2",                 51,  51,  31 * 60, 240, []),
    ];

    /// <summary>Evenly spaced rarities between the rarest and the most common locked one.</summary>
    private static double[] Spread(int count, double lowest, double highest) =>
        Enumerable.Range(0, count)
            .Select(i => count == 1 ? lowest : lowest + (highest - lowest) * i / (count - 1))
            .ToArray();

    private static FixtureGame Build(
        uint appId, string name, int unlocked, int total, int playtimeMinutes,
        int lastPlayedDaysAgo, IReadOnlyList<double> lockedPercents)
    {
        // Turns the otherwise-redundant `total` into a guard: the counts here
        // are transcribed from the mockup by hand, and a typo would show up as
        // a quietly wrong completion percentage rather than as a failure.
        if (unlocked + lockedPercents.Count != total)
        {
            throw new InvalidOperationException(
                $"{name}: {unlocked} unlocked + {lockedPercents.Count} locked != {total} total");
        }

        var achievements = new List<AchievementProgress>();

        for (var i = 0; i < unlocked; i++)
        {
            achievements.Add(new AchievementProgress(
                $"ACH_{i}", AchievementName(appId, i), "Unlocked already",
                Icon(appId, i), IsHidden: false, Unlocked: true,
                Now.AddDays(-14 - i * 3), 20 + Rng.NextDouble() * 40));
        }

        for (var i = 0; i < lockedPercents.Count; i++)
        {
            var hidden = i == lockedPercents.Count - 1 && lockedPercents.Count > 2;

            achievements.Add(new AchievementProgress(
                $"LOCK_{i}", hidden ? "Hidden achievement" : AchievementName(appId, unlocked + i),
                hidden ? string.Empty : "Do the difficult thing under the difficult condition",
                Icon(appId, unlocked + i), hidden, Unlocked: false, null, lockedPercents[i]));
        }

        return new FixtureGame(
            new OwnedGame(appId, name, string.Empty, playtimeMinutes, 0, Now.AddDays(-lastPlayedDaysAgo)),
            achievements);
    }

    /// <summary>A game where Steam has not backfilled rarity for part of the list.</summary>
    private static FixtureGame BuildUnknown(
        uint appId, string name, int unlocked, int total, int playtimeMinutes,
        int lastPlayedDaysAgo, int unknownCount)
    {
        var locked = total - unlocked;
        var known = Spread(locked - unknownCount, 1.2, 30.0);

        // Build only sees the achievements with known rarity, so its total has
        // to exclude the unknowns appended below or its guard would fire.
        var built = Build(appId, name, unlocked, total - unknownCount,
            playtimeMinutes, lastPlayedDaysAgo, known);

        var withUnknowns = built.Achievements.Concat(
            Enumerable.Range(0, unknownCount).Select(i => new AchievementProgress(
                $"UNK_{i}", AchievementName(appId, 900 + i), "Rarity not published by Steam",
                Icon(appId, 900 + i), IsHidden: false, Unlocked: false, null, null)))
            .ToList();

        return built with { Achievements = withUnknowns };
    }

    private static string AchievementName(uint appId, int index) => $"Achievement {appId % 1000}-{index:D2}";

    private static string Icon(uint appId, int index) =>
        $"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{appId}/missing-{index}.jpg";
}
