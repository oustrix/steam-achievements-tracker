# UI Screens Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the six screens of the design mockup as Blazor components in `SteamAchievements.UI`, backed by a unit-tested presentation layer in `SteamAchievements.Core`, viewable locally on macOS through a development-only host.

**Architecture:** All display logic — the "why it is here" sentence, effort labels, relative dates, sorting — lives in `Core/Presentation` as pure functions and is covered by ordinary xUnit tests. Razor components are thin renderers over the resulting records. A read-only SQLite connection separate from the sync engine's connection feeds them through one interface, `ILibraryQuery`.

**Tech Stack:** .NET 10 (SDK 10.0.302), Blazor (Razor Class Library + ASP.NET Core Blazor Server for preview), SQLite via Dapper, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-26-ui-screens-design.md`. Read it before starting. The design source is vendored in Task 1.

## Global Constraints

- **Language:** everything committed is English — code, comments, UI strings, commit messages. No exceptions.
- **Platform:** work happens on macOS. `SteamAchievements.Windows` does not compile here and is never touched by this plan. Never run a bare `dotnet test` or `dotnet build` at the repository root — it fails with NETSDK1100 because the solution contains a `net10.0-windows` project. Always name the project.
- **The only local verification command:** `dotnet test SteamAchievements.Core.Tests`.
- **Boundary rule:** no `Microsoft.Win32`, no `System.Security.Cryptography.ProtectedData`, nothing Windows-only in `Core` or `UI`.
- **Purity rule:** everything in `Core/Presentation` is a pure function. `now` is always a parameter. Never call `DateTimeOffset.UtcNow` there.
- **Dapper facts that have already cost debugging time:** Dapper cannot map into `ValueTuple` and does not translate `snake_case` to PascalCase — alias every column explicitly (`SELECT app_id AS AppId`). SQLite reports every INTEGER column as `Int64`, and Dapper's record materializer needs an exact CLR type match — row records use `long` and narrow in the projection. Declaring `uint` or `int` there throws at runtime on multi-row queries.
- **Rare threshold:** an achievement is *rare* when its global percentage is below **5.0%**. This single constant governs every generated sentence.
- **Accent default:** `#e0a355`.
- **Commit after every task.** Do not batch.

---

## File Structure

**Created in `SteamAchievements.Core/Presentation/`:**

| File | Responsibility |
|---|---|
| `Formatting.cs` | Playtime, relative and absolute dates, durations, thousands, number words |
| `ReasonWriter.cs` | The "why it is here" sentence, and nothing else |
| `QueueView.cs` | `QueueView`, `QueueRow`, `LibrarySummary` records |
| `QueueRowBuilder.cs` | `OwnedGame` + achievements → `QueueRow`; effort label and bar maths |
| `GameDetailView.cs` | `GameDetailView`, `AchievementRow` records |
| `GameDetailBuilder.cs` | Achievement ordering, rarity bars, hidden-achievement copy |
| `SyncRunView.cs` | One row of sync history |
| `ILibraryQuery.cs` | The read seam between data and UI |
| `IUserPreferences.cs` | The one write the UI makes |

**Modified in `SteamAchievements.Core/`:**

| File | Change |
|---|---|
| `Data/Database.cs` | `OpenRead`, an idempotent column helper, `sync_runs`, `settings.accent` |
| `Analytics/EffortCalculator.cs` | `UnknownRarityCost` becomes public |

**Created in `SteamAchievements.Core/Data/`:** `SqliteLibraryQuery.cs`, `SqliteUserPreferences.cs`.

**`SteamAchievements.UI/`** — template boilerplate deleted, replaced by `wwwroot/app.css`, `wwwroot/fonts/`, `wwwroot/queue-scroll.js`, `State/QueueState.cs`, and component folders `Layout/`, `Queue/`, `Game/`, `Sync/`, `Settings/`, `Onboarding/`, `Shared/`.

**`SteamAchievements.Preview/`** — new development-only web host with `Fixtures/`.

---

## Task 1: Design reference and formatting primitives

Vendors the design source into the repository so later tasks have the exact palette, copy and numbers to work from, then builds the string helpers every screen depends on.

**Files:**
- Create: `docs/design/steam-achievements-tracker.dc.html`
- Create: `SteamAchievements.Core/Presentation/Formatting.cs`
- Test: `SteamAchievements.Core.Tests/Presentation/FormattingTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `SteamAchievements.Core.Presentation.Formatting` with `Count(int)`, `Number(long)`, `Percent(double)`, `Playtime(int minutes)`, `Date(DateTimeOffset)`, `Relative(DateTimeOffset value, DateTimeOffset now)`, `Timestamp(DateTimeOffset value, DateTimeOffset now)`, `Duration(long milliseconds)`.

- [ ] **Step 1: Vendor the design reference**

The mockup is the source of truth for palette, copy and layout. Save it into the repository so no later task has to guess. Retrieve it from the Claude Design project (`70a57881-f220-456d-bfa1-337e7ab231f7`, file `Steam Achievements Tracker.dc.html`) and write it verbatim to `docs/design/steam-achievements-tracker.dc.html`.

Add a sibling `docs/design/README.md`:

```markdown
# Design reference

`steam-achievements-tracker.dc.html` is the design mockup this UI was built
from, exported from Claude Design. It is a reference document, not a build
artifact: it is read as text for palette values, copy, spacing and layout.

It will not render on its own — it needs the Claude Design runtime
(`support.js`), which is deliberately not vendored here. Nothing in the build
or the tests depends on this file.
```

- [ ] **Step 2: Write the failing tests**

Create `SteamAchievements.Core.Tests/Presentation/FormattingTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~FormattingTests`
Expected: FAIL — the build breaks with CS0246, `Formatting` does not exist.

- [ ] **Step 4: Implement `Formatting`**

Create `SteamAchievements.Core/Presentation/Formatting.cs`:

```csharp
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
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~FormattingTests`
Expected: PASS, 8 tests plus 11 theory cases.

- [ ] **Step 6: Commit**

```bash
git add docs/design SteamAchievements.Core/Presentation/Formatting.cs SteamAchievements.Core.Tests/Presentation/FormattingTests.cs
git commit -m "feat: add presentation formatting primitives and vendor the design reference"
```

---

## Task 2: The "why it is here" sentence

The single most product-defining string in the application. It is generated, so its rules have to be exact.

**Files:**
- Create: `SteamAchievements.Core/Presentation/ReasonWriter.cs`
- Test: `SteamAchievements.Core.Tests/Presentation/ReasonWriterTests.cs`

**Interfaces:**
- Consumes: `Formatting.Count`, `Formatting.Number`, `Formatting.Percent`, `Formatting.Date` from Task 1. `AchievementProgress` from `SteamAchievements.Core.Data`.
- Produces: `ReasonWriter.Write(IReadOnlyList<AchievementProgress>) -> string` and `ReasonWriter.RareThreshold` (`const double`, 5.0).

- [ ] **Step 1: Write the failing tests**

Create `SteamAchievements.Core.Tests/Presentation/ReasonWriterTests.cs`:

```csharp
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests.Presentation;

public class ReasonWriterTests
{
    private static AchievementProgress Locked(double? percent) =>
        new("api", "Name", "desc", "icon", false, false, null, percent);

    private static AchievementProgress Unlocked(double? percent, DateTimeOffset? at = null) =>
        new("api", "Name", "desc", "icon", false, true, at ?? DateTimeOffset.UnixEpoch, percent);

    [Fact]
    public void ReportsTheLastUnlockDateForAFinishedGame()
    {
        var reason = ReasonWriter.Write(
        [
            Unlocked(50, new DateTimeOffset(2025, 11, 11, 0, 0, 0, TimeSpan.Zero)),
            Unlocked(10, new DateTimeOffset(2026, 3, 24, 0, 0, 0, TimeSpan.Zero)),
        ]);

        Assert.Equal("Complete — last unlock 24 Mar 2026", reason);
    }

    [Fact]
    public void SaysOnlyCompleteWhenNoUnlockDateWasEverRecorded()
    {
        // Steam returns unlocktime 0 for achievements unlocked before it started
        // recording timestamps; the repository stores that as null.
        var reason = ReasonWriter.Write([new("api", "N", "d", "i", false, true, null, 50)]);

        Assert.Equal("Complete", reason);
    }

    [Fact]
    public void SaysRarityIsUnknownForAllOfThemWhenNoPercentagesExist()
    {
        var reason = ReasonWriter.Write(Enumerable.Range(0, 39).Select(_ => Locked(null)).ToList());

        Assert.Equal("39 left, rarity unknown for all of them", reason);
    }

    [Fact]
    public void CountsHowManyLackRarityWhenOnlySomeDo()
    {
        var achievements = Enumerable.Range(0, 33).Select(_ => Locked(20.0))
            .Concat(Enumerable.Range(0, 6).Select(_ => Locked(null)))
            .ToList();

        // "six", not "6": the mockup writes a digit here and a word two lines
        // later ("four below 5% of owners"). It is hand-written prose and
        // inconsistent with itself; the rule is words up to nine everywhere
        // inside the clause. Do not "fix" this back to match the mockup.
        Assert.Equal("39 left, rarity unknown for six of them", ReasonWriter.Write(achievements));
    }

    [Fact]
    public void NamesThePercentageWhenExactlyOneRareAchievementIsLeftInAShortList()
    {
        var reason = ReasonWriter.Write([Locked(30.0), Locked(25.0), Locked(2.1)]);

        Assert.Equal("3 left: two common, one rare (2.1%)", reason);
    }

    [Fact]
    public void HandlesTheSingleRemainingRareAchievementWithoutASpuriousCommonClause()
    {
        Assert.Equal("1 left: one rare (2.1%)", ReasonWriter.Write([Locked(2.1)]));
    }

    [Fact]
    public void FallsBackToCountingWhenMoreThanFourAreLeftEvenWithASingleRareOne()
    {
        // The naming form only reads well for a short list; with five left the
        // sentence would be "5 left: four common, one rare (2.1%)", which is
        // more arithmetic than the reader asked for.
        var achievements = Enumerable.Range(0, 4).Select(_ => Locked(30.0))
            .Append(Locked(2.1)).ToList();

        Assert.Equal("5 left, one below 5% of owners", ReasonWriter.Write(achievements));
    }

    [Fact]
    public void CountsRareAchievementsWhenThereAreSeveral()
    {
        var achievements = Enumerable.Range(0, 12).Select(_ => Locked(30.0))
            .Concat(Enumerable.Range(0, 4).Select(_ => Locked(1.9)))
            .ToList();

        Assert.Equal("16 left, four below 5% of owners", ReasonWriter.Write(achievements));
    }

    [Fact]
    public void ReportsTheFloorOfTheLowestPercentageWhenNothingIsRare()
    {
        var achievements = new[] { Locked(8.4), Locked(12.0), Locked(30.0) }
            .Concat(Enumerable.Range(0, 3).Select(_ => Locked(40.0)))
            .ToList();

        Assert.Equal("6 left, all above 8% of owners", ReasonWriter.Write(achievements));
    }

    [Fact]
    public void TreatsExactlyFivePercentAsCommonNotRare()
    {
        // The threshold is "below 5%", so 5.0 itself must not trip it.
        Assert.Equal("2 left, all above 5% of owners",
            ReasonWriter.Write([Locked(5.0), Locked(9.0)]));

        Assert.Equal("2 left: one common, one rare (4.9%)",
            ReasonWriter.Write([Locked(4.9), Locked(9.0)]));
    }

    [Fact]
    public void IgnoresUnlockedAchievementsWhenDescribingWhatIsLeft()
    {
        var reason = ReasonWriter.Write([Unlocked(1.0), Locked(30.0), Locked(40.0)]);

        Assert.Equal("2 left, all above 30% of owners", reason);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~ReasonWriterTests`
Expected: FAIL — `ReasonWriter` does not exist.

- [ ] **Step 3: Implement `ReasonWriter`**

Create `SteamAchievements.Core/Presentation/ReasonWriter.cs`:

```csharp
using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Presentation;

/// <summary>
/// Writes the one-line explanation of why a game sits where it does in the
/// queue. Without it a ranked list reads as guesswork.
///
/// The wording describes percentages and never achievability. "one rare
/// (2.1%)" reports that 2.1% of owners hold the achievement; it deliberately
/// does not say whether that is hard, dead, or worth attempting. See design
/// doc section 8.1 for why that distinction is load-bearing.
/// </summary>
public static class ReasonWriter
{
    /// <summary>
    /// An achievement is rare below this share of owners. One threshold, not
    /// several: the design mockup varies between 8%, 5%, 2% and 1% because it
    /// is hand-written prose, and a generator needs a single rule.
    /// </summary>
    public const double RareThreshold = 5.0;

    /// <summary>The naming form only reads well while the list is short.</summary>
    private const int NamedRarityLimit = 4;

    public static string Write(IReadOnlyList<AchievementProgress> achievements)
    {
        var locked = achievements.Where(a => !a.Unlocked).ToList();

        if (locked.Count == 0)
        {
            // Max over a nullable projection yields null for an empty sequence,
            // which is exactly the "no timestamps recorded" case.
            var last = achievements.Max(a => a.UnlockedAt);
            return last is null ? "Complete" : $"Complete — last unlock {Formatting.Date(last.Value)}";
        }

        // The leading count is always digits: it is the sentence's headline
        // number. Counts inside the clause use words up to nine.
        var head = $"{Formatting.Number(locked.Count)} left";

        var unknown = locked.Count(a => a.GlobalPercent is null);

        if (unknown == locked.Count)
        {
            return $"{head}, rarity unknown for all of them";
        }

        if (unknown > 0)
        {
            return $"{head}, rarity unknown for {Formatting.Count(unknown)} of them";
        }

        var rare = locked.Where(a => a.GlobalPercent!.Value < RareThreshold).ToList();

        if (rare.Count == 1 && locked.Count <= NamedRarityLimit)
        {
            var common = locked.Count - 1;
            var percent = Formatting.Percent(rare[0].GlobalPercent!.Value);

            return common == 0
                ? $"{head}: one rare ({percent})"
                : $"{head}: {Formatting.Count(common)} common, one rare ({percent})";
        }

        if (rare.Count > 0)
        {
            return $"{head}, {Formatting.Count(rare.Count)} below {RareThreshold:0}% of owners";
        }

        var lowest = locked.Min(a => a.GlobalPercent!.Value);
        return $"{head}, all above {Math.Floor(lowest):0}% of owners";
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~ReasonWriterTests`
Expected: PASS, 11 tests.

- [ ] **Step 5: Commit**

```bash
git add SteamAchievements.Core/Presentation/ReasonWriter.cs SteamAchievements.Core.Tests/Presentation/ReasonWriterTests.cs
git commit -m "feat: generate the queue's why-it-is-here explanation"
```

---

## Task 3: Queue rows

**Files:**
- Create: `SteamAchievements.Core/Presentation/QueueView.cs`
- Create: `SteamAchievements.Core/Presentation/QueueRowBuilder.cs`
- Test: `SteamAchievements.Core.Tests/Presentation/QueueRowBuilderTests.cs`

**Interfaces:**
- Consumes: `ReasonWriter.Write` (Task 2), `Formatting` (Task 1), `EffortCalculator.Evaluate` and `GameEffort` from `SteamAchievements.Core.Analytics`, `OwnedGame` from `SteamAchievements.Core.Steam`.
- Produces:
  - `QueueRow(uint AppId, string Name, int Unlocked, int Total, int CompletionPercent, double Effort, string EffortText, string EffortLabel, string Reason, int PlaytimeHours, bool Complete, bool RarityUnknown)`
  - `QueueView(IReadOnlyList<QueueRow> Rows, int TotalGames)`
  - `LibrarySummary(int GameCount, int AchievementCount, string CountsText, string LastSyncText)`
  - `QueueRowBuilder.Build(OwnedGame, IReadOnlyList<AchievementProgress>) -> QueueRow`
  - `QueueRowBuilder.EffortLabel(double) -> string`
  - `QueueRowBuilder.EffortBarPercent(double effort, double maxEffort) -> int`

- [ ] **Step 1: Write the failing tests**

Create `SteamAchievements.Core.Tests/Presentation/QueueRowBuilderTests.cs`:

```csharp
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Presentation;

public class QueueRowBuilderTests
{
    private static OwnedGame Game(int playtimeMinutes = 5040) =>
        new(367520, "Hollow Knight", "hash", playtimeMinutes, 0,
            new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero));

    private static AchievementProgress Locked(double? percent) =>
        new("api", "Name", "desc", "icon", false, false, null, percent);

    private static AchievementProgress Unlocked(double percent) =>
        new("api", "Name", "desc", "icon", false, true, DateTimeOffset.UnixEpoch, percent);

    [Theory]
    [InlineData(0.0, "an evening")]
    [InlineData(7.9, "an evening")]
    [InlineData(8.0, "a few sessions")]
    [InlineData(24.9, "a few sessions")]
    [InlineData(25.0, "a long haul")]
    [InlineData(79.9, "a long haul")]
    [InlineData(80.0, "a project")]
    [InlineData(342.9, "a project")]
    public void LabelsEffortInHumanTerms(double effort, string expected)
    {
        Assert.Equal(expected, QueueRowBuilder.EffortLabel(effort));
    }

    [Fact]
    public void ScalesTheEffortBarLogarithmicallySoLargeGamesDoNotFlattenSmallOnes()
    {
        // Linear scaling against Europa Universalis IV's 342 units would render
        // a 4.2-unit game at 1% — indistinguishable from empty.
        var small = QueueRowBuilder.EffortBarPercent(4.2, 342.9);
        var large = QueueRowBuilder.EffortBarPercent(342.9, 342.9);

        Assert.Equal(100, large);
        Assert.InRange(small, 20, 35);
    }

    [Fact]
    public void NeverRendersANonZeroEffortAsAnEmptyTrack()
    {
        Assert.Equal(4, QueueRowBuilder.EffortBarPercent(0.01, 342.9));
    }

    [Fact]
    public void GivesACompletedGameNoBarAtAll()
    {
        Assert.Equal(0, QueueRowBuilder.EffortBarPercent(0, 342.9));
    }

    [Fact]
    public void SurvivesAListWhereEveryGameHasZeroEffort()
    {
        Assert.Equal(0, QueueRowBuilder.EffortBarPercent(0, 0));
    }

    [Fact]
    public void BuildsARowFromAGameAndItsAchievements()
    {
        var row = QueueRowBuilder.Build(Game(), [Unlocked(60), Unlocked(50), Locked(2.1)]);

        Assert.Equal(367520u, row.AppId);
        Assert.Equal("Hollow Knight", row.Name);
        Assert.Equal(2, row.Unlocked);
        Assert.Equal(3, row.Total);
        Assert.Equal(67, row.CompletionPercent);
        Assert.Equal(84, row.PlaytimeHours);
        Assert.False(row.Complete);
        Assert.False(row.RarityUnknown);
        Assert.Equal("1 left: one rare (2.1%)", row.Reason);
    }

    [Fact]
    public void MarksAFullyUnlockedGameCompleteAndLabelsItSo()
    {
        var row = QueueRowBuilder.Build(Game(), [Unlocked(60), Unlocked(50)]);

        Assert.True(row.Complete);
        Assert.Equal(100, row.CompletionPercent);
        Assert.Equal("0", row.EffortText);
        Assert.Equal("complete", row.EffortLabel);
    }

    [Fact]
    public void FormatsEffortWithAtMostOneDecimal()
    {
        var row = QueueRowBuilder.Build(Game(), [Unlocked(80), Locked(20), Locked(20)]);

        // Two locked at 20% against a 80% maximum: -log2(0.25) = 2 each.
        Assert.Equal("4", row.EffortText);
    }

    [Fact]
    public void PropagatesTheUnknownRarityFlagFromTheCalculator()
    {
        var row = QueueRowBuilder.Build(Game(), [Locked(null), Locked(null)]);

        Assert.True(row.RarityUnknown);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~QueueRowBuilderTests`
Expected: FAIL — `QueueRowBuilder` does not exist.

- [ ] **Step 3: Create the records**

Create `SteamAchievements.Core/Presentation/QueueView.cs`:

```csharp
namespace SteamAchievements.Core.Presentation;

/// <summary>
/// One row of the completion queue, with every displayed value already
/// resolved to a string. The bar width is deliberately absent: it depends on
/// the largest effort in the currently visible list, which changes with every
/// filter keystroke and is therefore computed by the screen, not here.
/// </summary>
public sealed record QueueRow(
    uint   AppId,
    string Name,
    int    Unlocked,
    int    Total,
    int    CompletionPercent,
    double Effort,
    string EffortText,
    string EffortLabel,
    string Reason,
    int    PlaytimeHours,
    bool   Complete,
    bool   RarityUnknown);

/// <summary>
/// <paramref name="TotalGames"/> counts the whole library, including games
/// with no achievements at all, because that is the denominator the mockup
/// shows: "12 of 1 482 games".
/// </summary>
public sealed record QueueView(IReadOnlyList<QueueRow> Rows, int TotalGames);

public sealed record LibrarySummary(
    int    GameCount,
    int    AchievementCount,
    string CountsText,
    string LastSyncText);
```

- [ ] **Step 4: Implement the builder**

Create `SteamAchievements.Core/Presentation/QueueRowBuilder.cs`:

```csharp
using System.Globalization;
using SteamAchievements.Core.Analytics;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Presentation;

public static class QueueRowBuilder
{
    /// <summary>Smallest visible bar, so a nearly-finished game still reads as started.</summary>
    private const int MinimumBarPercent = 4;

    public static QueueRow Build(OwnedGame game, IReadOnlyList<AchievementProgress> achievements)
    {
        var effort = EffortCalculator.Evaluate(achievements);
        var complete = effort.TotalCount > 0 && effort.RemainingCount == 0;

        return new QueueRow(
            game.AppId,
            game.Name,
            effort.UnlockedCount,
            effort.TotalCount,
            (int)Math.Round(effort.CompletionPercent),
            effort.RemainingEffort,
            effort.RemainingEffort.ToString("0.#", CultureInfo.InvariantCulture),
            complete ? "complete" : EffortLabel(effort.RemainingEffort),
            ReasonWriter.Write(achievements),
            game.PlaytimeForever / 60,
            complete,
            effort.RarityUnknown);
    }

    /// <summary>
    /// Turns an abstract effort number into something a person can plan around.
    /// The thresholds come from the design mockup.
    /// </summary>
    public static string EffortLabel(double effort) => effort switch
    {
        < 8 => "an evening",
        < 25 => "a few sessions",
        < 80 => "a long haul",
        _ => "a project",
    };

    /// <summary>
    /// Bar width on a logarithmic scale against the largest effort currently
    /// on screen. Linear scaling would collapse everything below the biggest
    /// game in the library into an invisible sliver — Europa Universalis IV
    /// carries 342 units against Hollow Knight's 4.2.
    /// </summary>
    public static int EffortBarPercent(double effort, double maxEffort)
    {
        if (effort <= 0 || maxEffort <= 0)
        {
            return 0;
        }

        var scaled = 100 * Math.Log(1 + effort) / Math.Log(1 + maxEffort);
        return Math.Max(MinimumBarPercent, (int)Math.Round(scaled));
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~QueueRowBuilderTests`
Expected: PASS, 9 tests plus 8 theory cases.

- [ ] **Step 6: Commit**

```bash
git add SteamAchievements.Core/Presentation/QueueView.cs SteamAchievements.Core/Presentation/QueueRowBuilder.cs SteamAchievements.Core.Tests/Presentation/QueueRowBuilderTests.cs
git commit -m "feat: build completion queue rows from games and achievements"
```

---

## Task 4: Game detail

**Files:**
- Create: `SteamAchievements.Core/Presentation/GameDetailView.cs`
- Create: `SteamAchievements.Core/Presentation/GameDetailBuilder.cs`
- Modify: `SteamAchievements.Core/Analytics/EffortCalculator.cs` (make `UnknownRarityCost` public)
- Test: `SteamAchievements.Core.Tests/Presentation/GameDetailBuilderTests.cs`

**Interfaces:**
- Consumes: `Formatting` (Task 1), `EffortCalculator.Cost`, `EffortCalculator.Evaluate`, `EffortCalculator.UnknownRarityCost`, `QueueRowBuilder.EffortLabel` (Task 3).
- Produces:
  - `AchievementRow(string Name, string Description, string IconUrl, bool Hidden, double? GlobalPercent, string PercentText, int RarityBarPercent, string CostText, string? UnlockedDateText)`
  - `GameDetailView(uint AppId, string Name, string PlaytimeText, string LastPlayedText, int Unlocked, int Total, int CompletionPercent, string EffortText, string EffortLabel, int Remaining, string RarestText, IReadOnlyList<AchievementRow> RemainingAchievements, IReadOnlyList<AchievementRow> UnlockedAchievements)`
  - `GameDetailBuilder.Build(OwnedGame, IReadOnlyList<AchievementProgress>, DateTimeOffset now) -> GameDetailView`

- [ ] **Step 1: Make the unknown-rarity cost public**

In `SteamAchievements.Core/Analytics/EffortCalculator.cs`, change the declaration from `private const double UnknownRarityCost = 1;` to `public const double UnknownRarityCost = 1;` and extend its doc comment with one sentence:

```csharp
    /// <summary>
    /// Cost assigned to a locked achievement whose global percent is unknown.
    /// Matches the equal-weight treatment used when a whole game has no rarity
    /// data at all — an unknown percent must never be treated as a verified
    /// zero, which would wrongly claim maximal rarity.
    ///
    /// Public because the game screen orders individual achievements by the
    /// same cost and must agree with the total shown above the list.
    /// </summary>
    public const double UnknownRarityCost = 1;
```

- [ ] **Step 2: Write the failing tests**

Create `SteamAchievements.Core.Tests/Presentation/GameDetailBuilderTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~GameDetailBuilderTests`
Expected: FAIL — `GameDetailBuilder` does not exist.

- [ ] **Step 4: Create the records**

Create `SteamAchievements.Core/Presentation/GameDetailView.cs`:

```csharp
namespace SteamAchievements.Core.Presentation;

/// <summary>
/// One achievement as the game screen shows it. Used for both the remaining
/// and the unlocked list; <paramref name="UnlockedDateText"/> is null for the
/// former.
/// </summary>
public sealed record AchievementRow(
    string  Name,
    string  Description,
    string  IconUrl,
    bool    Hidden,
    double? GlobalPercent,
    string  PercentText,
    int     RarityBarPercent,
    string  CostText,
    string? UnlockedDateText);

public sealed record GameDetailView(
    uint   AppId,
    string Name,
    string PlaytimeText,
    string LastPlayedText,
    int    Unlocked,
    int    Total,
    int    CompletionPercent,
    string EffortText,
    string EffortLabel,
    int    Remaining,
    string RarestText,
    IReadOnlyList<AchievementRow> RemainingAchievements,
    IReadOnlyList<AchievementRow> UnlockedAchievements);
```

- [ ] **Step 5: Implement the builder**

Create `SteamAchievements.Core/Presentation/GameDetailBuilder.cs`:

```csharp
using System.Globalization;
using SteamAchievements.Core.Analytics;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Presentation;

public static class GameDetailBuilder
{
    private const string HiddenDescription = "Steam returns no description for hidden achievements";

    public static GameDetailView Build(
        OwnedGame game, IReadOnlyList<AchievementProgress> achievements, DateTimeOffset now)
    {
        var effort = EffortCalculator.Evaluate(achievements);

        // The same normalisation EffortCalculator uses, so the rarity bars and
        // the cost figures tell one story instead of two.
        var known = achievements.Where(a => a.GlobalPercent is > 0).ToList();
        var maxPercent = known.Count == 0 ? 0 : known.Max(a => a.GlobalPercent!.Value);

        var locked = achievements.Where(a => !a.Unlocked).ToList();

        var remaining = locked
            .OrderBy(a => Cost(a, maxPercent))
            .Select(a => Row(a, maxPercent))
            .ToList();

        var unlocked = achievements.Where(a => a.Unlocked)
            .OrderByDescending(a => a.UnlockedAt ?? DateTimeOffset.MinValue)
            .Select(a => Row(a, maxPercent))
            .ToList();

        var rarestPercent = locked
            .Where(a => a.GlobalPercent is not null)
            .Select(a => a.GlobalPercent!.Value)
            .DefaultIfEmpty(double.NaN)
            .Min();

        var complete = effort.TotalCount > 0 && effort.RemainingCount == 0;

        return new GameDetailView(
            game.AppId,
            game.Name,
            Formatting.Playtime(game.PlaytimeForever),
            game.LastPlayed is { } played ? Formatting.Relative(played, now) : "never",
            effort.UnlockedCount,
            effort.TotalCount,
            (int)Math.Round(effort.CompletionPercent),
            effort.RemainingEffort.ToString("0.#", CultureInfo.InvariantCulture),
            complete ? "complete" : QueueRowBuilder.EffortLabel(effort.RemainingEffort),
            effort.RemainingCount,
            double.IsNaN(rarestPercent) ? "unknown" : Formatting.Percent(rarestPercent),
            remaining,
            unlocked);
    }

    private static double Cost(AchievementProgress achievement, double maxPercent) =>
        achievement.GlobalPercent is { } percent
            ? EffortCalculator.Cost(percent, maxPercent)
            : EffortCalculator.UnknownRarityCost;

    private static AchievementRow Row(AchievementProgress achievement, double maxPercent)
    {
        var percent = achievement.GlobalPercent;

        var bar = percent is { } value && maxPercent > 0
            ? Math.Clamp((int)Math.Round(100 * value / maxPercent), 0, 100)
            : 0;

        // Steam almost always returns an empty description for hidden
        // achievements, and there is nowhere to source one in the MVP. Saying
        // so is more useful than an empty line.
        var description = achievement.IsHidden && string.IsNullOrWhiteSpace(achievement.Description)
            ? HiddenDescription
            : achievement.Description;

        return new AchievementRow(
            achievement.DisplayName,
            description,
            achievement.IconUrl,
            achievement.IsHidden,
            percent,
            percent is { } p ? $"{Formatting.Percent(p)} of owners" : "rarity unknown",
            bar,
            Cost(achievement, maxPercent).ToString("0.0", CultureInfo.InvariantCulture),
            achievement.UnlockedAt is { } at ? Formatting.Date(at) : null);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~GameDetailBuilderTests`
Expected: PASS, 10 tests.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: PASS. Making `UnknownRarityCost` public must not have disturbed the existing 74 tests.

- [ ] **Step 8: Commit**

```bash
git add SteamAchievements.Core/Presentation/GameDetailView.cs SteamAchievements.Core/Presentation/GameDetailBuilder.cs SteamAchievements.Core/Analytics/EffortCalculator.cs SteamAchievements.Core.Tests/Presentation/GameDetailBuilderTests.cs
git commit -m "feat: build the game detail view from achievement progress"
```

---

## Task 5: Schema changes and user preferences

Adds the sync history table, the accent column, a migration helper for columns, a second connection for reading, and the one write the UI makes.

**Files:**
- Modify: `SteamAchievements.Core/Data/Database.cs`
- Create: `SteamAchievements.Core/Presentation/IUserPreferences.cs`
- Create: `SteamAchievements.Core/Data/SqliteUserPreferences.cs`
- Test: `SteamAchievements.Core.Tests/Data/DatabaseMigrationTests.cs`
- Test: `SteamAchievements.Core.Tests/Data/SqliteUserPreferencesTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `Database.OpenRead(string path) -> SqliteConnection`; the `sync_runs` table; `settings.accent`; `IUserPreferences` with `string? Accent { get; }` and `void SetAccent(string accent)`; `SqliteUserPreferences(SqliteConnection)`.

- [ ] **Step 1: Write the failing tests**

Create `SteamAchievements.Core.Tests/Data/DatabaseMigrationTests.cs`:

```csharp
using Dapper;
using Microsoft.Data.Sqlite;
using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Tests.Data;

public class DatabaseMigrationTests
{
    [Fact]
    public void CreatesTheSyncRunsTable()
    {
        using var connection = Database.Open(":memory:");

        var tables = connection.Query<string>(
            "SELECT name FROM sqlite_master WHERE type = 'table'").ToHashSet();

        Assert.Contains("sync_runs", tables);
    }

    [Fact]
    public void AddsTheAccentColumnToASettingsTableThatPredatesIt()
    {
        // Simulates an existing installation: settings exists in its original
        // shape, and CREATE TABLE IF NOT EXISTS will leave it untouched.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        connection.Execute("""
            CREATE TABLE settings (
                id                 INTEGER PRIMARY KEY CHECK (id = 1),
                steam_id64         TEXT,
                persona_name       TEXT,
                avatar_url         TEXT,
                last_full_sync_at  TEXT
            );
            """);

        Database.Migrate(connection);

        var columns = connection.Query<string>("SELECT name FROM pragma_table_info('settings')")
            .ToHashSet();
        Assert.Contains("accent", columns);
    }

    [Fact]
    public void RunningTheMigrationTwiceIsANoOp()
    {
        using var connection = Database.Open(":memory:");

        Database.Migrate(connection);   // must not throw "duplicate column name"

        var columns = connection.Query<string>("SELECT name FROM pragma_table_info('settings')")
            .Count(name => name == "accent");
        Assert.Equal(1, columns);
    }

    [Fact]
    public void OpenReadReturnsAUsableConnectionWithoutMigrating()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sat-{Guid.NewGuid():N}.db");

        try
        {
            using (var writer = Database.Open(path))
            {
                writer.Execute("INSERT INTO games (app_id, name) VALUES (620, 'Portal 2')");
            }

            using var reader = Database.OpenRead(path);

            Assert.Equal("Portal 2",
                reader.QuerySingle<string>("SELECT name FROM games WHERE app_id = 620"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadingAndWritingThroughSeparateConnectionsDoesNotConflict()
    {
        // The UI reads through its own connection while the sync engine writes
        // through another. WAL permits exactly this; without it the reader
        // would block or fail.
        var path = Path.Combine(Path.GetTempPath(), $"sat-{Guid.NewGuid():N}.db");

        try
        {
            using var writer = Database.Open(path);
            using var reader = Database.OpenRead(path);

            writer.Execute("INSERT INTO games (app_id, name) VALUES (620, 'Portal 2')");

            Assert.Equal(1, reader.QuerySingle<long>("SELECT COUNT(*) FROM games"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

Create `SteamAchievements.Core.Tests/Data/SqliteUserPreferencesTests.cs`:

```csharp
using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Tests.Data;

public class SqliteUserPreferencesTests
{
    [Fact]
    public void ReportsNoAccentBeforeOneWasEverChosen()
    {
        using var connection = Database.Open(":memory:");
        var preferences = new SqliteUserPreferences(connection);

        Assert.Null(preferences.Accent);
    }

    [Fact]
    public void RoundTripsTheChosenAccent()
    {
        using var connection = Database.Open(":memory:");
        var preferences = new SqliteUserPreferences(connection);

        preferences.SetAccent("#8fb3c9");

        Assert.Equal("#8fb3c9", preferences.Accent);
    }

    [Fact]
    public void OverwritesAPreviouslyChosenAccentInsteadOfAddingARow()
    {
        using var connection = Database.Open(":memory:");
        var preferences = new SqliteUserPreferences(connection);

        preferences.SetAccent("#8fb3c9");
        preferences.SetAccent("#a8b58c");

        Assert.Equal("#a8b58c", preferences.Accent);
        Assert.Equal(1, Dapper.SqlMapper.QuerySingle<long>(connection, "SELECT COUNT(*) FROM settings"));
    }

    [Fact]
    public void PreservesOtherSettingsColumnsWhenWritingTheAccent()
    {
        using var connection = Database.Open(":memory:");
        Dapper.SqlMapper.Execute(connection,
            "INSERT INTO settings (id, persona_name) VALUES (1, 'oustrix')");

        new SqliteUserPreferences(connection).SetAccent("#c98f7a");

        Assert.Equal("oustrix", Dapper.SqlMapper.QuerySingle<string>(
            connection, "SELECT persona_name FROM settings WHERE id = 1"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SteamAchievements.Core.Tests --filter "FullyQualifiedName~DatabaseMigrationTests|FullyQualifiedName~SqliteUserPreferencesTests"`
Expected: FAIL — `Database.Migrate` is private, `Database.OpenRead` and `SqliteUserPreferences` do not exist.

- [ ] **Step 3: Extend `Database`**

In `SteamAchievements.Core/Data/Database.cs`, make three changes.

First, add `OpenRead` next to `Open`:

```csharp
    /// <summary>
    /// A second connection for readers — the UI — alongside the sync engine's
    /// own. <see cref="GameRepository"/> is not thread-safe and
    /// <c>SyncOrchestrator</c> already serializes every call to it behind a
    /// lock; sharing that connection with the UI would put reads back inside
    /// the same contention. WAL lets a reader run concurrently with the
    /// writer, so the UI simply gets its own handle.
    ///
    /// Deliberately not <c>Mode=ReadOnly</c>: a read-only SQLite connection to
    /// a WAL database still needs write access to the shared-memory index
    /// file, so that mode fails in exactly the configuration this is for. The
    /// guarantee here is by construction — callers issue only SELECTs — and
    /// the name says so.
    ///
    /// Skips <see cref="Migrate"/>: schema ownership belongs to the writer.
    /// </summary>
    public static SqliteConnection OpenRead(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        connection.Execute("PRAGMA busy_timeout = 5000;");
        return connection;
    }
```

Second, change `private static void Migrate` to `public static void Migrate`, and add above it:

```csharp
    /// <summary>
    /// Exposed so a migration can be applied to a connection that was opened
    /// by other means, and so tests can assert it is idempotent.
    /// </summary>
```

Third, inside `Migrate`, add the `sync_runs` table to the existing statement block, immediately after the `snapshots` table:

```sql
            CREATE TABLE IF NOT EXISTS sync_runs (
                started_at    TEXT PRIMARY KEY,
                kind          TEXT    NOT NULL,
                games_synced  INTEGER NOT NULL,
                duration_ms   INTEGER NOT NULL,
                error         TEXT
            );
```

and after the whole statement block, add the column migration plus its helper:

```csharp
        // CREATE TABLE IF NOT EXISTS cannot add a column to a table that
        // already exists, so anything added to an existing table after the
        // first release needs this path.
        EnsureColumn(connection, "settings", "accent", "TEXT");
    }

    /// <summary>
    /// Idempotent ALTER TABLE. SQLite has no "ADD COLUMN IF NOT EXISTS", and
    /// running the same ALTER twice throws "duplicate column name", so the
    /// current shape is inspected first.
    /// </summary>
    private static void EnsureColumn(SqliteConnection connection, string table, string column, string type)
    {
        var existing = connection.Query<string>(
            $"SELECT name FROM pragma_table_info('{table}')").ToHashSet();

        if (!existing.Contains(column))
        {
            connection.Execute($"ALTER TABLE {table} ADD COLUMN {column} {type}");
        }
    }
```

- [ ] **Step 4: Create the preferences interface and its implementation**

Create `SteamAchievements.Core/Presentation/IUserPreferences.cs`:

```csharp
namespace SteamAchievements.Core.Presentation;

/// <summary>
/// The only thing the UI writes. Kept separate from <c>ILibraryQuery</c> so
/// that interface stays honestly read-only and the single write is visible in
/// the type system rather than buried in a general-purpose repository.
/// </summary>
public interface IUserPreferences
{
    /// <summary>The chosen accent colour, or null while the default applies.</summary>
    string? Accent { get; }

    void SetAccent(string accent);
}
```

Create `SteamAchievements.Core/Data/SqliteUserPreferences.cs`:

```csharp
using Dapper;
using Microsoft.Data.Sqlite;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Data;

/// <summary>
/// Owns a writable connection of its own. WAL allows readers alongside a
/// writer but still permits only one writer at a time, so a click on the
/// accent picker during a sync would otherwise fail with SQLITE_BUSY. The
/// connection this is constructed with is expected to carry a busy timeout;
/// a single-row update against <c>settings</c> finishes in microseconds, so
/// waiting out a sync transaction is invisible.
/// </summary>
public sealed class SqliteUserPreferences : IUserPreferences
{
    private readonly SqliteConnection _connection;

    public SqliteUserPreferences(SqliteConnection connection) => _connection = connection;

    public string? Accent =>
        _connection.QuerySingleOrDefault<string?>("SELECT accent FROM settings WHERE id = 1");

    public void SetAccent(string accent) => _connection.Execute("""
        INSERT INTO settings (id, accent) VALUES (1, @Accent)
        ON CONFLICT(id) DO UPDATE SET accent = excluded.accent;
        """, new { Accent = accent });
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SteamAchievements.Core.Tests --filter "FullyQualifiedName~DatabaseMigrationTests|FullyQualifiedName~SqliteUserPreferencesTests"`
Expected: PASS, 9 tests.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: PASS. `MigrationCreatesAllTables` in `GameRepositoryTests` still passes; the new table is additive.

- [ ] **Step 7: Commit**

```bash
git add SteamAchievements.Core/Data/Database.cs SteamAchievements.Core/Data/SqliteUserPreferences.cs SteamAchievements.Core/Presentation/IUserPreferences.cs SteamAchievements.Core.Tests/Data/DatabaseMigrationTests.cs SteamAchievements.Core.Tests/Data/SqliteUserPreferencesTests.cs
git commit -m "feat: add sync history table, accent setting and a reader connection"
```

---

## Task 6: The read seam

One interface between stored data and every screen, and the SQLite implementation behind it.

**Files:**
- Create: `SteamAchievements.Core/Presentation/SyncRunView.cs`
- Create: `SteamAchievements.Core/Presentation/ILibraryQuery.cs`
- Create: `SteamAchievements.Core/Data/SqliteLibraryQuery.cs`
- Test: `SteamAchievements.Core.Tests/Data/SqliteLibraryQueryTests.cs`

**Interfaces:**
- Consumes: `QueueRowBuilder.Build` (Task 3), `GameDetailBuilder.Build` (Task 4), `Formatting` (Task 1), `Database.OpenRead` (Task 5).
- Produces:
  - `SyncRunView(string WhenText, string WhatText, string DurationText, bool Failed)`
  - `ILibraryQuery` with `QueueView GetQueue(DateTimeOffset now)`, `GameDetailView? GetGame(uint appId, DateTimeOffset now)`, `LibrarySummary GetSummary(DateTimeOffset now)`, `IReadOnlyList<SyncRunView> GetSyncHistory(int limit, DateTimeOffset now)`
  - `SqliteLibraryQuery(SqliteConnection)`

- [ ] **Step 1: Write the failing tests**

Create `SteamAchievements.Core.Tests/Data/SqliteLibraryQueryTests.cs`:

```csharp
using Dapper;
using Microsoft.Data.Sqlite;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Data;

public class SqliteLibraryQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Seeds a database through the write path, exactly as a sync would, so
    /// the query is exercised against real stored shapes rather than hand-made
    /// rows. Returns the same connection: ":memory:" is per-connection, so a
    /// second handle would see an empty database.
    /// </summary>
    private static SqliteConnection Seed()
    {
        var connection = Database.Open(":memory:");
        var repository = new GameRepository(connection);

        repository.UpsertOwnedGames(
        [
            new OwnedGame(367520, "Hollow Knight", "hk", 5040, 0, Now.AddDays(-3)),
            new OwnedGame(620, "Portal 2", "p2", 1860, 0, Now.AddDays(-240)),
            new OwnedGame(431960, "Wallpaper Engine", "we", 60, 0, null),
        ]);

        repository.UpsertSchema(367520,
        [
            new AchievementSchema("HK_1", "Attuned", "Beat the Trial of the Warrior", "i1", "g1", false, 0),
            new AchievementSchema("HK_2", "Steel Soul", "Complete the game in Steel Soul mode", "i2", "g2", false, 1),
        ], Now.AddDays(-1));

        repository.UpsertGlobalPercentages(367520,
            new Dictionary<string, double> { ["HK_1"] = 28.0, ["HK_2"] = 9.8 });

        repository.UpsertPlayerAchievements(367520,
        [
            new PlayerAchievement("HK_1", Unlocked: true, Now.AddDays(-14)),
            new PlayerAchievement("HK_2", Unlocked: false, null),
        ]);

        repository.UpsertSchema(620,
            [new AchievementSchema("P2_1", "Bridge Over Troubling Water", "Solve it", "i", "g", false, 0)],
            Now.AddDays(-1));
        repository.UpsertGlobalPercentages(620, new Dictionary<string, double> { ["P2_1"] = 41.0 });
        repository.UpsertPlayerAchievements(620,
            [new PlayerAchievement("P2_1", Unlocked: true, Now.AddDays(-200))]);

        return connection;
    }

    [Fact]
    public void ReturnsOneRowPerGameThatHasAchievements()
    {
        using var connection = Seed();

        var queue = new SqliteLibraryQuery(connection).GetQueue(Now);

        // Wallpaper Engine has no achievements and must not appear as a row,
        // but it is still part of the library and counts in the total.
        Assert.Equal(["Hollow Knight", "Portal 2"], queue.Rows.Select(r => r.Name).Order());
        Assert.Equal(3, queue.TotalGames);
    }

    [Fact]
    public void CarriesTheGeneratedExplanationThroughToTheRow()
    {
        using var connection = Seed();

        var row = new SqliteLibraryQuery(connection).GetQueue(Now).Rows
            .Single(r => r.AppId == 367520);

        Assert.Equal(1, row.Unlocked);
        Assert.Equal(2, row.Total);
        Assert.Equal("1 left, all above 9% of owners", row.Reason);
        Assert.Equal(84, row.PlaytimeHours);
    }

    [Fact]
    public void MarksAFullyUnlockedGameComplete()
    {
        using var connection = Seed();

        var row = new SqliteLibraryQuery(connection).GetQueue(Now).Rows.Single(r => r.AppId == 620);

        Assert.True(row.Complete);
    }

    [Fact]
    public void ReturnsTheGameDetailForAKnownAppId()
    {
        using var connection = Seed();

        var game = new SqliteLibraryQuery(connection).GetGame(367520, Now);

        Assert.NotNull(game);
        Assert.Equal("Hollow Knight", game.Name);
        Assert.Equal("Steel Soul", game.RemainingAchievements.Single().Name);
        Assert.Equal("Attuned", game.UnlockedAchievements.Single().Name);
    }

    [Fact]
    public void ReturnsNullForAnAppIdThatIsNotInTheLibrary()
    {
        using var connection = Seed();

        Assert.Null(new SqliteLibraryQuery(connection).GetGame(1, Now));
    }

    [Fact]
    public void HandlesAGameWithNoRarityDataAtAll()
    {
        using var connection = Seed();
        new GameRepository(connection).UpsertSchema(435150,
            [new AchievementSchema("D_1", "Rise", "desc", "i", "g", false, 0)], Now);

        var row = new SqliteLibraryQuery(connection).GetQueue(Now).Rows.Single(r => r.AppId == 435150);

        Assert.True(row.RarityUnknown);
        Assert.Equal("1 left, rarity unknown for all of them", row.Reason);
    }

    [Fact]
    public void SummarisesTheLibrary()
    {
        using var connection = Seed();
        connection.Execute(
            "INSERT INTO settings (id, last_full_sync_at) VALUES (1, @At)",
            new { At = Now.AddMinutes(-14).ToString("o") });

        var summary = new SqliteLibraryQuery(connection).GetSummary(Now);

        Assert.Equal(3, summary.GameCount);
        Assert.Equal(3, summary.AchievementCount);
        Assert.Equal("3 games · 3 ach.", summary.CountsText);
        Assert.Equal("Last sync 14 min ago", summary.LastSyncText);
    }

    [Fact]
    public void SaysSoWhenNothingHasEverBeenSynced()
    {
        using var connection = Seed();

        Assert.Equal("Never synced", new SqliteLibraryQuery(connection).GetSummary(Now).LastSyncText);
    }

    [Fact]
    public void ReadsSyncHistoryMostRecentFirst()
    {
        using var connection = Seed();
        connection.Execute("""
            INSERT INTO sync_runs (started_at, kind, games_synced, duration_ms) VALUES
                (@Older, 'full', 1482, 531000),
                (@Newer, 'incremental', 4, 2149);
            """,
            new
            {
                Older = Now.AddDays(-4).ToString("o"),
                Newer = Now.AddHours(-2).ToString("o"),
            });

        var history = new SqliteLibraryQuery(connection).GetSyncHistory(10, Now);

        Assert.Equal("Incremental — 4 games changed", history[0].WhatText);
        Assert.Equal("2.1 s", history[0].DurationText);
        // Composed rather than written out: Formatting.Number separates
        // thousands with a thin space (U+2009), and a literal ASCII space here
        // would fail the comparison for a reason invisible in the diff.
        Assert.Equal($"Full sync — {Formatting.Number(1482)} games", history[1].WhatText);
        Assert.Equal("8 min 51 s", history[1].DurationText);
    }

    [Fact]
    public void ReportsAnEmptyHistoryRatherThanInventingRows()
    {
        using var connection = Seed();

        Assert.Empty(new SqliteLibraryQuery(connection).GetSyncHistory(10, Now));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~SqliteLibraryQueryTests`
Expected: FAIL — `SqliteLibraryQuery` does not exist.

- [ ] **Step 3: Create `SyncRunView` and `ILibraryQuery`**

Create `SteamAchievements.Core/Presentation/SyncRunView.cs`:

```csharp
namespace SteamAchievements.Core.Presentation;

public sealed record SyncRunView(
    string WhenText,
    string WhatText,
    string DurationText,
    bool   Failed);
```

Create `SteamAchievements.Core/Presentation/ILibraryQuery.cs`:

```csharp
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
```

- [ ] **Step 4: Implement `SqliteLibraryQuery`**

Create `SteamAchievements.Core/Data/SqliteLibraryQuery.cs`:

```csharp
using Dapper;
using Microsoft.Data.Sqlite;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Data;

/// <summary>
/// Reads the whole library in two queries and assembles the views in memory.
///
/// It deliberately does not go through <see cref="GameRepository"/>: that type
/// wraps the sync engine's connection, is not thread-safe, and every call to
/// it is already serialized behind a lock. This one owns a separate reader
/// connection from <see cref="Database.OpenRead"/> instead.
/// </summary>
public sealed class SqliteLibraryQuery : ILibraryQuery
{
    private readonly SqliteConnection _connection;

    public SqliteLibraryQuery(SqliteConnection connection) => _connection = connection;

    // Row records use long for every INTEGER column and narrow in the
    // projection: Microsoft.Data.Sqlite reports INTEGER as Int64 and Dapper's
    // record materializer needs an exact CLR type match.
    private sealed record GameRow(long AppId, string Name, long PlaytimeForever,
        long PlaytimeTwoWeeks, string? LastPlayedAt);

    private sealed record ProgressRow(long AppId, string ApiName, string DisplayName,
        string Description, string IconUrl, long IsHidden, long? Unlocked,
        string? UnlockedAt, double? Percent);

    private sealed record SyncRunRow(string StartedAt, string Kind, long GamesSynced,
        long DurationMs, string? Error);

    private const string GamesSql = """
        SELECT g.app_id            AS AppId,
               g.name              AS Name,
               o.playtime_forever  AS PlaytimeForever,
               o.playtime_2weeks   AS PlaytimeTwoWeeks,
               o.last_played_at    AS LastPlayedAt
        FROM owned_games o JOIN games g ON g.app_id = o.app_id
        """;

    /// <summary>
    /// One projection for both callers. The whole-library and single-game
    /// reads differ only by a WHERE clause, and writing them out twice is how
    /// a new column ends up added to one and forgotten in the other.
    /// </summary>
    private static string ProgressSql(bool singleGame) => $"""
        SELECT a.app_id        AS AppId,
               a.api_name      AS ApiName,
               a.display_name  AS DisplayName,
               a.description   AS Description,
               a.icon_url      AS IconUrl,
               a.is_hidden     AS IsHidden,
               p.unlocked      AS Unlocked,
               p.unlocked_at   AS UnlockedAt,
               gp.percent      AS Percent
        FROM achievements a
        LEFT JOIN player_achievements p  ON p.app_id  = a.app_id AND p.api_name  = a.api_name
        LEFT JOIN global_percents     gp ON gp.app_id = a.app_id AND gp.api_name = a.api_name
        {(singleGame ? "WHERE a.app_id = @AppId" : "")}
        ORDER BY a.app_id, a.sort_order
        """;

    public QueueView GetQueue(DateTimeOffset now)
    {
        var games = _connection.Query<GameRow>(GamesSql).ToList();
        var progress = _connection.Query<ProgressRow>(ProgressSql(singleGame: false))
            .GroupBy(r => r.AppId)
            .ToDictionary(g => g.Key, g => g.Select(Project).ToList());

        var rows = games
            .Where(g => progress.ContainsKey(g.AppId))
            .Select(g => QueueRowBuilder.Build(Game(g), progress[g.AppId]))
            .ToList();

        // The denominator the mockup shows is the whole library, including the
        // 30-40% of it that has no achievements at all.
        return new QueueView(rows, games.Count);
    }

    public GameDetailView? GetGame(uint appId, DateTimeOffset now)
    {
        var game = _connection.QuerySingleOrDefault<GameRow>(
            $"{GamesSql} WHERE g.app_id = @AppId", new { AppId = appId });

        if (game is null)
        {
            return null;
        }

        var achievements = _connection
            .Query<ProgressRow>(ProgressSql(singleGame: true), new { AppId = appId })
            .Select(Project)
            .ToList();

        return GameDetailBuilder.Build(Game(game), achievements, now);
    }

    public LibrarySummary GetSummary(DateTimeOffset now)
    {
        var games = (int)_connection.QuerySingle<long>("SELECT COUNT(*) FROM owned_games");
        var achievements = (int)_connection.QuerySingle<long>("SELECT COUNT(*) FROM achievements");
        var lastSync = _connection.QuerySingleOrDefault<string?>(
            "SELECT last_full_sync_at FROM settings WHERE id = 1");

        return new LibrarySummary(
            games,
            achievements,
            $"{Formatting.Number(games)} games · {Formatting.Number(achievements)} ach.",
            lastSync is null
                ? "Never synced"
                : $"Last sync {Formatting.Relative(DateTimeOffset.Parse(lastSync), now)}");
    }

    public IReadOnlyList<SyncRunView> GetSyncHistory(int limit, DateTimeOffset now) =>
        _connection.Query<SyncRunRow>("""
            SELECT started_at    AS StartedAt,
                   kind          AS Kind,
                   games_synced  AS GamesSynced,
                   duration_ms   AS DurationMs,
                   error         AS Error
            FROM sync_runs ORDER BY started_at DESC LIMIT @Limit
            """, new { Limit = limit })
            .Select(r => new SyncRunView(
                Formatting.Timestamp(DateTimeOffset.Parse(r.StartedAt), now),
                Describe(r),
                Formatting.Duration(r.DurationMs),
                r.Error is not null))
            .ToList();

    private static string Describe(SyncRunRow run)
    {
        if (run.Error is not null)
        {
            return $"Failed — {run.Error}";
        }

        var count = Formatting.Number(run.GamesSynced);

        return run.Kind switch
        {
            "full" => $"Full sync — {count} games",
            "incremental" => $"Incremental — {count} games changed",
            "schema" => $"Schema refresh — {count} games stale",
            _ => $"{run.Kind} — {count} games",
        };
    }

    private static OwnedGame Game(GameRow row) => new(
        (uint)row.AppId, row.Name, string.Empty,
        (int)row.PlaytimeForever, (int)row.PlaytimeTwoWeeks,
        row.LastPlayedAt is null ? null : DateTimeOffset.Parse(row.LastPlayedAt));

    private static AchievementProgress Project(ProgressRow row) => new(
        row.ApiName, row.DisplayName, row.Description, row.IconUrl,
        row.IsHidden == 1, row.Unlocked == 1,
        row.UnlockedAt is null ? null : DateTimeOffset.Parse(row.UnlockedAt),
        row.Percent);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~SqliteLibraryQueryTests`
Expected: PASS, 10 tests.

- [ ] **Step 6: Run the whole suite and format**

Run: `dotnet test SteamAchievements.Core.Tests && dotnet format SteamAchievements.Core`
Expected: tests PASS; `dotnet format` makes no or only whitespace changes.

- [ ] **Step 7: Commit**

```bash
git add SteamAchievements.Core/Presentation SteamAchievements.Core/Data/SqliteLibraryQuery.cs SteamAchievements.Core.Tests/Data/SqliteLibraryQueryTests.cs
git commit -m "feat: add the read seam between stored data and the screens"
```

At this point the entire presentation layer exists and is tested. Nothing has been rendered yet.

---

## Task 7: UI project foundation

Clears the template boilerplate and lays down the palette and typography every component builds on.

**Files:**
- Modify: `SteamAchievements.UI/SteamAchievements.UI.csproj`
- Modify: `SteamAchievements.UI/_Imports.razor`
- Delete: `SteamAchievements.UI/Component1.razor`, `Component1.razor.css`, `ExampleJsInterop.cs`, `wwwroot/background.png`, `wwwroot/exampleJsInterop.js`
- Create: `SteamAchievements.UI/wwwroot/app.css`
- Create: `SteamAchievements.UI/wwwroot/fonts/fonts.css` and the woff2 files beside it
- Create: `SteamAchievements.UI/wwwroot/fonts/OFL.txt`

**Interfaces:**
- Consumes: nothing.
- Produces: the CSS custom properties every component's isolated stylesheet references, served at `_content/SteamAchievements.UI/app.css`.

- [ ] **Step 1: Delete the template boilerplate and wire the project up**

```bash
git rm SteamAchievements.UI/Component1.razor SteamAchievements.UI/Component1.razor.css \
       SteamAchievements.UI/ExampleJsInterop.cs \
       SteamAchievements.UI/wwwroot/background.png SteamAchievements.UI/wwwroot/exampleJsInterop.js
```

Replace `SteamAchievements.UI/SteamAchievements.UI.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <!--
    No <SupportedPlatform Include="browser" />. This library is hosted by
    BlazorWebView, not WebAssembly, and that marker only points the
    platform-compatibility analyzer at Core's SQLite dependency for no gain.
  -->

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.Web" Version="10.0.10" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\SteamAchievements.Core\SteamAchievements.Core.csproj" />
  </ItemGroup>

</Project>
```

Replace `SteamAchievements.UI/_Imports.razor` with:

```razor
@using System.Globalization
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using SteamAchievements.Core.Presentation
@using SteamAchievements.UI.Layout
@using SteamAchievements.UI.Shared
@using SteamAchievements.UI.State
```

- [ ] **Step 2: Verify the project still builds**

Run: `dotnet build SteamAchievements.UI`
Expected: succeeds. The `_Imports` namespaces do not exist yet as folders, but `@using` on a namespace with no types is not an error in Razor.

If it does fail with RZ10012 or CS0246 on the `SteamAchievements.UI.*` namespaces, create the folders with a placeholder `.gitkeep` — they are populated in Tasks 9 through 15.

- [ ] **Step 3: Vendor the fonts**

Google's CSS endpoint returns woff2 URLs when asked with a browser user agent. Fetch the stylesheet, pull the URLs out, download them, and rewrite the paths to point at the local copies.

```bash
mkdir -p SteamAchievements.UI/wwwroot/fonts
cd SteamAchievements.UI/wwwroot/fonts

UA='Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36'
URL='https://fonts.googleapis.com/css2?family=Instrument+Sans:ital,wght@0,400..700;1,400&family=JetBrains+Mono:wght@400;500;700&display=swap'

curl -sS -H "User-Agent: $UA" "$URL" -o fonts.css

# Download every referenced woff2 next to the stylesheet and point at it locally.
grep -o 'https://fonts.gstatic.com/[^)]*\.woff2' fonts.css | sort -u | while read -r u; do
  curl -sS "$u" -o "$(basename "$u")"
done
sed -i '' -E 's#https://fonts\.gstatic\.com/[^)]*/([^/)]+\.woff2)#\1#g' fonts.css

ls *.woff2 | head
cd -
```

Verify: `fonts.css` contains no `https://` references, and `ls SteamAchievements.UI/wwwroot/fonts/*.woff2` lists at least four files.

Both families are licensed under the SIL Open Font License. Save the licence text next to them:

```bash
curl -sS https://raw.githubusercontent.com/google/fonts/main/ofl/instrumentsans/OFL.txt \
  -o SteamAchievements.UI/wwwroot/fonts/OFL-InstrumentSans.txt
curl -sS https://raw.githubusercontent.com/JetBrains/JetBrainsMono/master/OFL.txt \
  -o SteamAchievements.UI/wwwroot/fonts/OFL-JetBrainsMono.txt
```

- [ ] **Step 4: Write the design tokens**

Create `SteamAchievements.UI/wwwroot/app.css`:

```css
/*
  Design tokens, taken from docs/design/steam-achievements-tracker.dc.html.
  Everything beyond this file lives in per-component isolated CSS. The mockup
  styles inline because it is a single-file prototype; carrying that into
  Razor would throw away the one mechanism that keeps styles from drifting.
*/

@import url("./fonts/fonts.css");

:root {
    --sans: "Instrument Sans", Helvetica, Arial, sans-serif;
    --mono: "JetBrains Mono", ui-monospace, SFMono-Regular, Menlo, monospace;

    --bg-page: #0e0d10;
    --bg-shell: #141317;
    --bg-sidebar: #121115;
    --bg-card: #18171c;
    --bg-raised: #1b1a20;
    --bg-selected: #1f1d26;
    --bg-active: #211f28;

    --border-hairline: #201e26;
    --border-subtle: #232028;
    --border: #2b2833;
    --border-strong: #3a3444;
    --border-hover: #544c60;

    --text: #edeae3;
    --text-secondary: #c6bfb4;
    --text-muted: #9c968c;
    --text-dim: #6f6a63;
    --text-faint: #57515e;

    /* Overridden on the shell root by the accent picker. */
    --accent: #e0a355;
    --accent-hover: #f0bd7c;
    --accent-dim: #7a6a52;
    --on-accent: #1a1509;

    --warn-bg: #211a12;
    --warn-border: #4a3b26;
    --warn-text: #f0bd7c;
    --warn-button: #2c2317;

    --danger-bg: #1f1517;
    --danger-border: #4a2626;
    --danger-text: #e89898;
    --danger-button: #2a191b;
    --danger-button-border: #6b3232;

    --track: #282531;
    --bar-complete: #4c4650;
    --dot-off: #3d3844;

    --placeholder: repeating-linear-gradient(135deg, #2b2833 0 6px, #232028 6px 12px);
}

* { box-sizing: border-box; }

html, body {
    margin: 0;
    padding: 0;
    height: 100%;
    background: var(--bg-page);
    color: var(--text);
    font-family: var(--sans);
    font-size: 14px;
    -webkit-font-smoothing: antialiased;
}

a { color: var(--accent); text-decoration: none; }
a:hover { color: var(--accent-hover); }

::-webkit-scrollbar { width: 10px; height: 10px; }
::-webkit-scrollbar-thumb { background: #34303a; border-radius: 6px; }
::-webkit-scrollbar-track { background: transparent; }

/* Uppercase micro-labels: "EFFORT", "HISTORY", "REMAINING EFFORT". */
.micro {
    font-family: var(--mono);
    font-size: 10px;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: var(--text-dim);
}

/* Every number, identifier and timestamp. The split between this and the
   sans face is what makes counts read as data rather than as copy. */
.num { font-family: var(--mono); }
```

- [ ] **Step 5: Commit**

```bash
git add -A SteamAchievements.UI
git commit -m "feat: replace the RCL template with design tokens and vendored fonts"
```

---

## Task 8: Preview host

A development-only host so every later task can be seen on macOS in seconds instead of through a three-minute Windows CI round trip. It renders the real components; it has no markup of its own.

**Files:**
- Create: `SteamAchievements.Preview/` (project, `Program.cs`, `Components/App.razor`, `Components/Routes.razor`, `Components/_Imports.razor`)
- Create: `SteamAchievements.Preview/Fixtures/FixtureData.cs`
- Create: `SteamAchievements.Preview/Fixtures/FixtureLibraryQuery.cs`
- Create: `SteamAchievements.Preview/Fixtures/InMemoryUserPreferences.cs`
- Create: `SteamAchievements.UI/Layout/AppShell.razor` (temporary shell, completed in Task 9)
- Create: `SteamAchievements.UI/Queue/QueuePage.razor` (temporary placeholder, completed in Task 10)
- Modify: `SteamAchievements.sln`

**Interfaces:**
- Consumes: `ILibraryQuery`, `IUserPreferences`, `QueueView`, `QueueRow`, `GameDetailView`, `LibrarySummary`, `SyncRunView` (Tasks 3-6).
- Produces: `SteamAchievements.Preview.Fixtures.FixtureData.Games` (a `IReadOnlyList<(OwnedGame Game, IReadOnlyList<AchievementProgress> Achievements)>`), and a running host at `http://localhost:5100`.

- [ ] **Step 1: Create the project and add it to the solution**

```bash
dotnet new blazor -n SteamAchievements.Preview --interactivity Server --empty --no-https
dotnet add SteamAchievements.Preview reference SteamAchievements.UI
dotnet sln add SteamAchievements.Preview
```

Add a note at the top of `SteamAchievements.Preview/SteamAchievements.Preview.csproj`, inside the `<Project>` element:

```xml
  <!--
    Development only. This host exists so the UI can be seen and iterated on
    from macOS, where SteamAchievements.Windows does not compile. It is built
    in CI to catch component breakage early and is never published.
  -->
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
```

Pin the port by replacing `SteamAchievements.Preview/Properties/launchSettings.json` with:

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5100",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

- [ ] **Step 2: Write the fixtures**

Create `SteamAchievements.Preview/Fixtures/FixtureData.cs`. The fourteen games are the mockup's, with their real app ids so the cover art loaded from Steam's CDN is real and the layout is checked against real proportions.

```csharp
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
```

Note the achievement icon URLs are intentionally non-resolving: they exercise the image fallback path, which is the harder case to get right.

Create `SteamAchievements.Preview/Fixtures/FixtureLibraryQuery.cs`:

```csharp
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>
/// Scenarios exist so the empty and error states can be seen without
/// genuinely breaking an API key. They shape the data the screens receive;
/// they are not a gallery of state cards.
/// </summary>
public enum Scenario
{
    Normal,
    Empty,
    InvalidKey,
    PrivateProfile,
    RarityUnknown,
    OtherAccount,
}

public sealed class FixtureLibraryQuery : ILibraryQuery
{
    public Scenario Scenario { get; set; } = Scenario.Normal;

    private IReadOnlyList<FixtureGame> Source => Scenario switch
    {
        Scenario.Empty or Scenario.PrivateProfile or Scenario.InvalidKey => [],
        Scenario.RarityUnknown => FixtureData.All.Where(g => g.Game.AppId == 435150).ToList(),
        _ => FixtureData.All,
    };

    public QueueView GetQueue(DateTimeOffset now)
    {
        var rows = Source.Select(g => QueueRowBuilder.Build(g.Game, g.Achievements)).ToList();

        // The mockup's denominator counts the whole library, achievements or not.
        return new QueueView(rows, rows.Count == 0 ? 0 : 1482);
    }

    public GameDetailView? GetGame(uint appId, DateTimeOffset now)
    {
        var game = Source.FirstOrDefault(g => g.Game.AppId == appId);
        return game is null ? null : GameDetailBuilder.Build(game.Game, game.Achievements, now);
    }

    public LibrarySummary GetSummary(DateTimeOffset now)
    {
        if (Source.Count == 0)
        {
            return new LibrarySummary(0, 0, "0 games · 0 ach.", "Never synced");
        }

        var achievements = Source.Sum(g => g.Achievements.Count);

        return new LibrarySummary(1482, 61214,
            $"{Formatting.Number(1482)} games · {Formatting.Number(61214)} ach.",
            $"Last sync {Formatting.Relative(now.AddMinutes(-14), now)}");
    }

    public IReadOnlyList<SyncRunView> GetSyncHistory(int limit, DateTimeOffset now)
    {
        if (Source.Count == 0)
        {
            return [];
        }

        return new[]
        {
            (now.AddMinutes(-14),  "incremental", 4L,    2149L),
            (now.AddHours(-13),    "incremental", 1L,     910L),
            (now.AddDays(-4),      "full",     1482L,  531000L),
            (now.AddDays(-4),      "schema",    214L,   66000L),
        }
        .Take(limit)
        .Select(r => new SyncRunView(
            Formatting.Timestamp(r.Item1, now),
            r.Item2 switch
            {
                "full" => $"Full sync — {Formatting.Number(r.Item3)} games",
                "incremental" => $"Incremental — {Formatting.Number(r.Item3)} games changed",
                _ => $"Schema refresh — {Formatting.Number(r.Item3)} games stale",
            },
            Formatting.Duration(r.Item4),
            Failed: false))
        .ToList();
    }
}
```

Create `SteamAchievements.Preview/Fixtures/InMemoryUserPreferences.cs`:

```csharp
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>Preview-only: the accent picker works, it just does not survive a restart.</summary>
public sealed class InMemoryUserPreferences : IUserPreferences
{
    public string? Accent { get; private set; }

    public void SetAccent(string accent) => Accent = accent;
}
```

- [ ] **Step 3: Wire the host up**

Replace `SteamAchievements.Preview/Program.cs`:

```csharp
using SteamAchievements.Core.Presentation;
using SteamAchievements.Preview.Components;
using SteamAchievements.Preview.Fixtures;
using SteamAchievements.UI.State;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// One fixture query per browser session, so the scenario switch in the query
// string affects only the tab that set it.
builder.Services.AddScoped<FixtureLibraryQuery>();
builder.Services.AddScoped<ILibraryQuery>(s => s.GetRequiredService<FixtureLibraryQuery>());
builder.Services.AddScoped<IUserPreferences, InMemoryUserPreferences>();

// QueueState is registered in Task 10, which is where the type is created.
// Registering it here would leave this project not compiling until then.

// Frozen so the preview reads identically on every run.
builder.Services.AddScoped<IClock>(_ => new FixedClock(FixtureData.Now));

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now => now;
}
```

Create `SteamAchievements.UI/State/IClock.cs` — the components need *a* clock, and Core presentation deliberately refuses to own one:

```csharp
namespace SteamAchievements.UI.State;

/// <summary>
/// The screens need the current time to pass into ILibraryQuery, and
/// Core/Presentation deliberately refuses to read it. This is where that
/// boundary is crossed, in one place, so the preview host can freeze it.
/// </summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
```

Replace `SteamAchievements.Preview/Components/App.razor`:

```razor
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Steam Achievements Tracker — preview</title>
    <base href="/" />
    <link rel="stylesheet" href="_content/SteamAchievements.UI/app.css" />
    @* Bundles every component's isolated CSS from the referenced RCL. *@
    <link rel="stylesheet" href="SteamAchievements.Preview.styles.css" />
    <HeadOutlet @rendermode="InteractiveServer" />
</head>
<body>
    <Routes @rendermode="InteractiveServer" />
    <script src="_framework/blazor.web.js"></script>
    <script src="_content/SteamAchievements.UI/queue-scroll.js"></script>
</body>
</html>
```

Replace `SteamAchievements.Preview/Components/Routes.razor`:

```razor
@using SteamAchievements.UI.Layout

@* Routable components live in the RCL, so the router has to be told to look
   there as well as in this assembly. *@
<Router AppAssembly="typeof(Program).Assembly"
        AdditionalAssemblies="new[] { typeof(AppShell).Assembly }">
    <Found Context="routeData">
        <RouteView RouteData="routeData" DefaultLayout="typeof(AppShell)" />
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

- [ ] **Step 4: Add a temporary shell and page so the host has something to render**

Create `SteamAchievements.UI/Layout/AppShell.razor` — a stub, completed in Task 9:

```razor
@inherits LayoutComponentBase

<div class="shell">
    @Body
</div>
```

Create `SteamAchievements.UI/Layout/AppShell.razor.css`:

```css
.shell {
    min-height: 100vh;
    background: var(--bg-shell);
}
```

Create `SteamAchievements.UI/Queue/QueuePage.razor` — a stub, completed in Task 10:

```razor
@page "/"
@inject ILibraryQuery Library
@inject IClock Clock

<h1>Completion queue</h1>
<p class="num">@_queue.Rows.Count of @_queue.TotalGames games</p>

@code {
    private QueueView _queue = new([], 0);

    protected override void OnInitialized() => _queue = Library.GetQueue(Clock.Now);
}
```

Create an empty `SteamAchievements.UI/wwwroot/queue-scroll.js` — populated in Task 11:

```javascript
// Populated in Task 11: scrolling the keyboard-selected queue row into view.
window.queueScroll = {};
```

- [ ] **Step 5: Run the host and confirm it renders**

Run: `dotnet run --project SteamAchievements.Preview`

Open `http://localhost:5100`. Expected: the heading "Completion queue" and the line "14 of 1482 games" on the dark page background, in Instrument Sans.

If the fonts do not apply, check the browser network tab for a 404 on `_content/SteamAchievements.UI/app.css` — that means the RCL static assets are not being served and `app.UseStaticFiles()` is missing from `Program.cs`.

- [ ] **Step 6: Commit**

```bash
git add -A SteamAchievements.Preview SteamAchievements.UI SteamAchievements.sln
git commit -m "feat: add a development-only preview host with mockup fixtures"
```

---

## Task 9: Shell and shared components

The sidebar every screen sits inside, and the two components that carry every empty and error state in the application.

**Files:**
- Modify: `SteamAchievements.UI/Layout/AppShell.razor` and `.razor.css`
- Create: `SteamAchievements.UI/Layout/NavItem.razor` and `.razor.css`
- Create: `SteamAchievements.UI/Layout/SyncStatusCard.razor` and `.razor.css`
- Create: `SteamAchievements.UI/Shared/Notice.razor` and `.razor.css`
- Create: `SteamAchievements.UI/Shared/EmptyState.razor` and `.razor.css`

**Interfaces:**
- Consumes: `ILibraryQuery.GetSummary`, `IUserPreferences.Accent`, `IClock` (Tasks 6, 5, 8).
- Produces: `Notice` with parameters `Severity` (`NoticeSeverity.Info | Warning | Danger`), `Title`, `Body` (`RenderFragment?`), `ActionLabel`, `OnAction`; `EmptyState` with `Title` and `Body`; `NavItem` with `Href`, `Label`, `WithDot`.

- [ ] **Step 1: Build `NavItem`**

Create `SteamAchievements.UI/Layout/NavItem.razor`:

```razor
<NavLink class="nav-item" href="@Href" Match="@(Href == "/" ? NavLinkMatch.All : NavLinkMatch.Prefix)">
    @if (WithDot)
    {
        <span class="dot"></span>
    }
    @Label
</NavLink>

@code {
    [Parameter, EditorRequired] public string Href { get; set; } = "";
    [Parameter, EditorRequired] public string Label { get; set; } = "";
    [Parameter] public bool WithDot { get; set; } = true;
}
```

Create `SteamAchievements.UI/Layout/NavItem.razor.css`:

```css
.nav-item {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 9px 11px;
    border-radius: 7px;
    font-size: 13px;
    color: var(--text-muted);
    cursor: pointer;
}

.nav-item:hover { color: var(--text); }

.nav-item.active {
    background: var(--bg-active);
    color: var(--text);
}

.dot {
    width: 5px;
    height: 5px;
    border-radius: 5px;
    background: var(--dot-off);
    flex: none;
}

.nav-item.active .dot { background: var(--accent); }
```

`NavLink` adds the `active` class itself, which is why this needs no click handling of its own.

- [ ] **Step 2: Build `SyncStatusCard`**

Create `SteamAchievements.UI/Layout/SyncStatusCard.razor`:

```razor
<a class="card" href="sync">
    <span class="when">@Summary.LastSyncText</span>
    <span class="counts num">@Summary.CountsText</span>
</a>

@code {
    [Parameter, EditorRequired] public LibrarySummary Summary { get; set; } = default!;
}
```

Create `SteamAchievements.UI/Layout/SyncStatusCard.razor.css`:

```css
.card {
    display: flex;
    flex-direction: column;
    gap: 4px;
    padding: 11px 12px;
    border: 1px solid var(--border-subtle);
    border-radius: 8px;
    background: var(--bg-raised);
    color: var(--text-muted);
    font-size: 12px;
}

.card:hover { border-color: var(--border-strong); }
.when { color: var(--text); font-size: 12px; }
.counts { font-size: 11px; color: var(--text-dim); }
```

- [ ] **Step 3: Build the shell**

Replace `SteamAchievements.UI/Layout/AppShell.razor`:

```razor
@inherits LayoutComponentBase
@inject ILibraryQuery Library
@inject IUserPreferences Preferences
@inject IClock Clock

<div class="shell" style="--accent: @(Preferences.Accent ?? DefaultAccent)">
    <nav class="sidebar">
        <div class="brand">
            <div class="name">Achievements</div>
            <div class="version num">Tracker 0.1.0</div>
        </div>

        <div class="nav">
            <NavItem Href="/" Label="Completion queue" />
            <NavItem Href="sync" Label="Sync" />
            <NavItem Href="settings" Label="Settings" />
        </div>

        <div class="spacer"></div>

        <SyncStatusCard Summary="_summary" />
    </nav>

    <main class="content">
        @Body
    </main>
</div>

@code {
    /// <summary>Amber, matching the mockup's default.</summary>
    public const string DefaultAccent = "#e0a355";

    private LibrarySummary _summary = new(0, 0, "", "");

    protected override void OnInitialized() => _summary = Library.GetSummary(Clock.Now);
}
```

The mockup's sidebar also lists "Game", "Onboarding" and "Empty & error states". Those are mockup scaffolding: the game screen is reached by opening a queue row, onboarding is a first-run gate rather than a destination, and the states are rendered where their condition arises.

Replace `SteamAchievements.UI/Layout/AppShell.razor.css`:

```css
.shell {
    display: grid;
    grid-template-columns: 212px 1fr;
    min-height: 100vh;
    background: var(--bg-shell);
}

.sidebar {
    display: flex;
    flex-direction: column;
    padding: 20px 12px 14px;
    background: var(--bg-sidebar);
    border-right: 1px solid var(--border-subtle);
}

.brand {
    display: flex;
    flex-direction: column;
    gap: 3px;
    padding: 0 10px 22px;
}

.name { font-size: 14px; font-weight: 600; letter-spacing: -0.01em; }
.version { font-size: 11px; color: var(--text-dim); }

.nav { display: flex; flex-direction: column; gap: 2px; }
.spacer { flex: 1; }

.content { overflow: auto; position: relative; }
```

- [ ] **Step 4: Build `Notice`**

This one component carries every warning and error card in the application, so it is worth getting exactly right.

Create `SteamAchievements.UI/Shared/Notice.razor`:

```razor
<div class="notice @Severity.ToString().ToLowerInvariant()">
    <span class="dot"></span>
    <div class="text">
        <span class="title">@Title</span>
        @if (Body is not null)
        {
            <span class="body">@Body</span>
        }
    </div>
    <span class="spacer"></span>
    @if (ActionLabel is not null)
    {
        <button class="action" @onclick="OnAction">@ActionLabel</button>
    }
</div>

@code {
    [Parameter] public NoticeSeverity Severity { get; set; } = NoticeSeverity.Info;
    [Parameter, EditorRequired] public string Title { get; set; } = "";
    [Parameter] public RenderFragment? Body { get; set; }
    [Parameter] public string? ActionLabel { get; set; }
    [Parameter] public EventCallback OnAction { get; set; }
}
```

Create `SteamAchievements.UI/Shared/NoticeSeverity.cs`:

```csharp
namespace SteamAchievements.UI.Shared;

public enum NoticeSeverity
{
    Info,
    Warning,
    Danger,
}
```

Create `SteamAchievements.UI/Shared/Notice.razor.css`:

```css
.notice {
    display: flex;
    gap: 14px;
    align-items: flex-start;
    padding: 16px 18px;
    border-radius: 11px;
    border: 1px solid var(--border-subtle);
    background: var(--bg-card);
}

.dot {
    width: 7px;
    height: 7px;
    border-radius: 7px;
    margin-top: 7px;
    flex: none;
    background: var(--text-dim);
}

.text { display: flex; flex-direction: column; gap: 5px; }
.title { font-size: 14px; font-weight: 600; }
.body { font-size: 13px; color: var(--text-secondary); text-wrap: pretty; }
.spacer { flex: 1; }

.action {
    flex: none;
    font: inherit;
    font-size: 12px;
    padding: 9px 13px;
    border-radius: 7px;
    border: 1px solid var(--border);
    background: var(--bg-selected);
    color: var(--text-muted);
    cursor: pointer;
}

.action:hover { color: var(--text); }

.warning { border-color: var(--warn-border); background: var(--warn-bg); }
.warning .dot { background: var(--accent); }
.warning .title { color: var(--warn-text); }
.warning .action {
    border-color: var(--warn-border);
    background: var(--warn-button);
    color: var(--accent);
}

.danger { border-color: var(--danger-border); background: var(--danger-bg); }
.danger .dot { background: var(--danger-text); }
.danger .title { color: var(--danger-text); }
.danger .action {
    border-color: var(--danger-button-border);
    background: var(--danger-button);
    color: var(--danger-text);
}
```

- [ ] **Step 5: Build `EmptyState`**

Create `SteamAchievements.UI/Shared/EmptyState.razor`:

```razor
<div class="empty">
    <div class="glyph"></div>
    <span class="title">@Title</span>
    <span class="body">@Body</span>
</div>

@code {
    [Parameter, EditorRequired] public string Title { get; set; } = "";
    [Parameter, EditorRequired] public string Body { get; set; } = "";
}
```

Create `SteamAchievements.UI/Shared/EmptyState.razor.css`:

```css
.empty {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 12px;
    padding: 40px 24px;
    text-align: center;
    border: 1px solid var(--border-subtle);
    border-radius: 11px;
    background: var(--bg-card);
}

.glyph {
    width: 44px;
    height: 44px;
    border-radius: 10px;
    background: var(--placeholder);
}

.title { font-size: 16px; font-weight: 600; }

.body {
    font-size: 13px;
    color: var(--text-muted);
    max-width: 380px;
    text-wrap: pretty;
}
```

- [ ] **Step 6: See it**

Run: `dotnet run --project SteamAchievements.Preview` and open `http://localhost:5100`.

Expected: a 212px sidebar on the left with "Achievements / Tracker 0.1.0", three navigation entries with "Completion queue" highlighted and an amber dot, and the sync card pinned to the bottom reading "Last sync 14 min ago" over "1 482 games · 61 214 ach.".

- [ ] **Step 7: Commit**

```bash
git add -A SteamAchievements.UI
git commit -m "feat: add the application shell and the shared state components"
```

---

## Task 10: Completion queue

The main screen. Filtering and sorting are pure functions in Core and tested there; the component holds only the criteria.

**Files:**
- Create: `SteamAchievements.Core/Presentation/QueueFilter.cs`
- Test: `SteamAchievements.Core.Tests/Presentation/QueueFilterTests.cs`
- Create: `SteamAchievements.UI/State/QueueState.cs`
- Create: `SteamAchievements.UI/Queue/QueueToolbar.razor` and `.razor.css`
- Create: `SteamAchievements.UI/Queue/QueueRowCard.razor` and `.razor.css`
- Create: `SteamAchievements.UI/Shared/ProgressBar.razor` and `.razor.css`
- Create: `SteamAchievements.UI/Shared/CoverImage.razor` and `.razor.css`
- Modify: `SteamAchievements.UI/Queue/QueuePage.razor` (+ new `.razor.css`)

**Interfaces:**
- Consumes: `QueueRow`, `QueueView` (Task 3), `QueueRowBuilder.EffortBarPercent` (Task 3), `ILibraryQuery` (Task 6), `EmptyState` (Task 9).
- Produces:
  - `QueueSort` enum (`Effort`, `Completion`, `Playtime`)
  - `QueueCriteria(QueueSort Sort, bool Descending, string Query, int MinPlaytimeHours, bool HideComplete)`
  - `QueueFilter.Apply(IReadOnlyList<QueueRow>, QueueCriteria) -> IReadOnlyList<QueueRow>`
  - `QueueFilter.DefaultDescending(QueueSort) -> bool`
  - `QueueState` with `Criteria`, `SelectedAppId`, `Changed` event, `SortBy`, `SetQuery`, `SetMinPlaytime`, `ToggleComplete`, `Select`
  - `ProgressBar` with `Percent`, `Color`; `CoverImage` with `Url`, `FallbackLabel`

- [ ] **Step 1: Write the failing filter tests**

Create `SteamAchievements.Core.Tests/Presentation/QueueFilterTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~QueueFilterTests`
Expected: FAIL — `QueueFilter` does not exist.

- [ ] **Step 3: Implement `QueueFilter`**

Create `SteamAchievements.Core/Presentation/QueueFilter.cs`:

```csharp
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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~QueueFilterTests`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit the filter**

```bash
git add SteamAchievements.Core/Presentation/QueueFilter.cs SteamAchievements.Core.Tests/Presentation/QueueFilterTests.cs
git commit -m "feat: add queue filtering and sorting"
```

- [ ] **Step 6: Add `QueueState`**

Create `SteamAchievements.UI/State/QueueState.cs`:

```csharp
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

    public event Action? Changed;

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
        Changed?.Invoke();
    }

    private void Update(QueueCriteria criteria)
    {
        Criteria = criteria;
        Changed?.Invoke();
    }
}
```

Register it in `SteamAchievements.Preview/Program.cs`, next to the other scoped services:

```csharp
builder.Services.AddScoped<QueueState>();
```

- [ ] **Step 7: Add the two shared display components**

Create `SteamAchievements.UI/Shared/ProgressBar.razor`:

```razor
<div class="track" style="max-width: @(MaxWidth ?? "none"); height: @(Height)px">
    <div class="fill" style="width: @Percent%; background: @Color"></div>
</div>

@code {
    [Parameter, EditorRequired] public int Percent { get; set; }
    [Parameter] public string Color { get; set; } = "var(--accent)";
    [Parameter] public int Height { get; set; } = 3;
    [Parameter] public string? MaxWidth { get; set; }
}
```

Create `SteamAchievements.UI/Shared/ProgressBar.razor.css`:

```css
.track {
    border-radius: 3px;
    background: var(--track);
    overflow: hidden;
}

.fill { height: 100%; }
```

Create `SteamAchievements.UI/Shared/CoverImage.razor` — not every app id has a `library_600x900.jpg`, so the fallback is required, not decorative:

```razor
@if (_failed || string.IsNullOrEmpty(Url))
{
    <div class="cover placeholder">
        <span class="label num">@FallbackLabel</span>
    </div>
}
else
{
    <img class="cover" src="@Url" alt="@FallbackLabel" loading="lazy" @onerror="() => _failed = true" />
}

@code {
    [Parameter, EditorRequired] public string? Url { get; set; }
    [Parameter] public string FallbackLabel { get; set; } = "";

    private bool _failed;

    /// <summary>A new app id means a new image, so the previous failure no longer applies.</summary>
    protected override void OnParametersSet() => _failed = false;
}
```

Create `SteamAchievements.UI/Shared/CoverImage.razor.css`:

```css
.cover {
    width: 100%;
    height: 100%;
    border-radius: 6px;
    object-fit: cover;
    display: block;
}

.placeholder {
    background: repeating-linear-gradient(135deg, #232028 0 6px, #1d1b22 6px 12px);
    display: flex;
    align-items: flex-end;
    padding: 6px;
}

.label {
    font-size: 8px;
    line-height: 1.2;
    color: var(--text-faint);
    overflow: hidden;
}
```

`OnParametersSet` resetting `_failed` matters under virtualization: Blazor reuses the same component instance for a different row as the list scrolls, and a stale failure flag would blank out covers that are perfectly fine.

- [ ] **Step 8: Build the toolbar**

Create `SteamAchievements.UI/Queue/QueueToolbar.razor`:

```razor
<div class="toolbar">
    <div class="headline">
        <div class="title-block">
            <h1>Completion queue</h1>
            <p>Sorted by how much work is left, not by how far the bar looks.
               Rarity is a number — the call is yours.</p>
        </div>

        <div class="sorts micro">
            <span>sort</span>
            @foreach (var (sort, label) in Sorts)
            {
                <button class="sort @(State.Criteria.Sort == sort ? "on" : "")"
                        @onclick="() => State.SortBy(sort)">
                    @label <span class="arrow">@Arrow(sort)</span>
                </button>
            }
        </div>
    </div>

    <div class="filters">
        <div class="search">
            <span class="glass"></span>
            <input value="@State.Criteria.Query" placeholder="Search titles"
                   @oninput="e => State.SetQuery(e.Value?.ToString() ?? string.Empty)" />
        </div>

        <select class="playtime" value="@State.Criteria.MinPlaytimeHours"
                @onchange="e => State.SetMinPlaytime(int.Parse(e.Value?.ToString() ?? "0"))">
            <option value="0">Min. playtime: any</option>
            <option value="1">Min. playtime: 1 h</option>
            <option value="5">Min. playtime: 5 h</option>
            <option value="20">Min. playtime: 20 h</option>
        </select>

        <button class="toggle @(State.Criteria.HideComplete ? "on" : "")" @onclick="State.ToggleComplete">
            @(State.Criteria.HideComplete ? "100 % complete: hidden" : "100 % complete: shown")
        </button>

        <span class="spacer"></span>

        <span class="count num">
            @Formatting.Number(Shown) of @Formatting.Number(Total) games · ↑↓ to move, Enter to open
        </span>
    </div>
</div>

@code {
    [Parameter, EditorRequired] public QueueState State { get; set; } = default!;
    [Parameter, EditorRequired] public int Shown { get; set; }
    [Parameter, EditorRequired] public int Total { get; set; }

    private static readonly (QueueSort Sort, string Label)[] Sorts =
    [
        (QueueSort.Effort, "Effort"),
        (QueueSort.Completion, "Completion"),
        (QueueSort.Playtime, "Playtime"),
    ];

    private string Arrow(QueueSort sort) =>
        State.Criteria.Sort != sort ? "" : State.Criteria.Descending ? "↓" : "↑";
}
```

Create `SteamAchievements.UI/Queue/QueueToolbar.razor.css`:

```css
.toolbar {
    position: sticky;
    top: 0;
    z-index: 5;
    display: flex;
    flex-direction: column;
    gap: 16px;
    padding: 22px 30px 16px;
    background: color-mix(in srgb, var(--bg-shell) 93%, transparent);
    backdrop-filter: blur(8px);
    border-bottom: 1px solid var(--border-subtle);
}

.headline { display: flex; align-items: flex-end; gap: 16px; flex-wrap: wrap; }

.title-block { display: flex; flex-direction: column; gap: 5px; flex: 1; min-width: 240px; }
.title-block h1 { margin: 0; font-size: 22px; font-weight: 600; letter-spacing: -0.02em; }
.title-block p {
    margin: 0;
    font-size: 13px;
    color: var(--text-muted);
    max-width: 560px;
    text-wrap: pretty;
}

.sorts { display: flex; gap: 6px; align-items: center; }

.sort {
    display: flex;
    gap: 5px;
    align-items: center;
    font: inherit;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    padding: 6px 10px;
    border-radius: 6px;
    border: 1px solid var(--border);
    background: transparent;
    color: var(--text-dim);
    cursor: pointer;
}

.sort.on { background: var(--bg-active); color: var(--accent); }
.arrow { width: 7px; display: inline-block; }

.filters { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }

.search {
    display: flex;
    align-items: center;
    gap: 8px;
    flex: 1;
    min-width: 240px;
    max-width: 340px;
    padding: 8px 12px;
    border-radius: 8px;
    border: 1px solid var(--border);
    background: var(--bg-raised);
}

.glass {
    width: 11px;
    height: 11px;
    border: 1.5px solid var(--text-dim);
    border-radius: 11px;
    flex: none;
}

.search input {
    flex: 1;
    background: transparent;
    border: 0;
    outline: none;
    color: var(--text);
    font: inherit;
    font-size: 13px;
}

.playtime, .toggle {
    font: inherit;
    font-size: 12px;
    padding: 9px 12px;
    border-radius: 8px;
    border: 1px solid var(--border);
    background: var(--bg-raised);
    color: var(--text-muted);
    cursor: pointer;
}

.playtime:hover, .toggle:hover { border-color: var(--border-strong); color: var(--text); }
.toggle.on { border-color: var(--warn-border); color: var(--accent); }

.spacer { flex: 1; }
.count { font-size: 11px; color: var(--text-dim); }
```

- [ ] **Step 9: Build the row**

Create `SteamAchievements.UI/Queue/QueueRowCard.razor`:

```razor
<div class="row @(Selected ? "selected" : "") @(Row.Complete ? "done" : "")"
     role="button" tabindex="-1"
     @onclick="OnOpen" @onmouseenter="OnSelect">

    <div class="cover-slot">
        <CoverImage Url="@CoverUrl" FallbackLabel="@Row.Name" />
    </div>

    <div class="middle">
        <div class="heading">
            <span class="name">@Row.Name</span>
            <span class="counts num">@Row.Unlocked of @Row.Total</span>
        </div>
        <ProgressBar Percent="Row.CompletionPercent" MaxWidth="420px"
                     Color="@(Row.Complete ? "var(--bar-complete)" : "var(--accent)")" />
        <div class="reason">@Row.Reason</div>
    </div>

    <div class="effort">
        <div class="value num" style="color: @(Row.Complete ? "var(--text-dim)" : "var(--accent)")">
            @Row.EffortText
        </div>
        <div class="micro">effort</div>
        <ProgressBar Percent="BarPercent" Color="var(--accent-dim)" />
        <div class="label">@Row.EffortLabel</div>
    </div>
</div>

@code {
    [Parameter, EditorRequired] public QueueRow Row { get; set; } = default!;
    [Parameter, EditorRequired] public int BarPercent { get; set; }
    [Parameter] public bool Selected { get; set; }
    [Parameter] public EventCallback OnOpen { get; set; }
    [Parameter] public EventCallback OnSelect { get; set; }

    private string CoverUrl =>
        $"https://cdn.cloudflare.steamstatic.com/steam/apps/{Row.AppId}/library_600x900.jpg";
}
```

Create `SteamAchievements.UI/Queue/QueueRowCard.razor.css`:

```css
.row {
    display: grid;
    grid-template-columns: 64px 1fr auto;
    gap: 18px;
    align-items: center;
    padding: 12px 16px 12px 12px;
    border: 1px solid var(--border-subtle);
    border-radius: 10px;
    background: var(--bg-card);
    cursor: pointer;
    transition: background 120ms, border-color 120ms;
}

.row.selected { background: var(--bg-selected); border-color: #4a4256; }
.row.done { opacity: 0.6; }

.cover-slot { height: 96px; }

.middle { display: flex; flex-direction: column; gap: 8px; min-width: 0; }

.heading { display: flex; align-items: baseline; gap: 10px; min-width: 0; }

.name {
    font-size: 15px;
    font-weight: 600;
    letter-spacing: -0.01em;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}

.counts { font-size: 11px; color: var(--text-dim); flex: none; }
.reason { font-size: 13px; color: var(--text-secondary); }
.row.done .reason { color: #8d8579; }

.effort {
    display: flex;
    flex-direction: column;
    align-items: flex-end;
    gap: 7px;
    padding-left: 8px;
    width: 96px;
}

.effort .value { font-size: 20px; font-weight: 500; line-height: 1; }
.effort .label { font-size: 12px; color: var(--text-muted); }
.effort ::deep .track { width: 96px; }
```

- [ ] **Step 10: Assemble the page**

Replace `SteamAchievements.UI/Queue/QueuePage.razor`:

```razor
@page "/"
@implements IDisposable
@inject ILibraryQuery Library
@inject IClock Clock
@inject QueueState State
@inject NavigationManager Navigation

<PageTitle>Completion queue</PageTitle>

<QueueToolbar State="State" Shown="_rows.Count" Total="_queue.TotalGames" />

<div class="list">
    @if (_rows.Count == 0)
    {
        <EmptyState Title="@EmptyTitle" Body="@EmptyBody" />
    }
    else
    {
        @foreach (var row in _rows)
        {
            <QueueRowCard Row="row"
                          BarPercent="QueueRowBuilder.EffortBarPercent(row.Effort, _maxEffort)"
                          Selected="State.SelectedAppId == row.AppId"
                          OnOpen="() => Open(row.AppId)"
                          OnSelect="() => State.Select(row.AppId)" />
        }
    }
</div>

@code {
    private QueueView _queue = new([], 0);
    private IReadOnlyList<QueueRow> _rows = [];
    private double _maxEffort;

    protected override void OnInitialized()
    {
        _queue = Library.GetQueue(Clock.Now);
        State.Changed += Refresh;
        Refresh();
    }

    public void Dispose() => State.Changed -= Refresh;

    private void Refresh()
    {
        _rows = QueueFilter.Apply(_queue.Rows, State.Criteria);

        // The bar scale is relative to what is on screen, so it is recomputed
        // whenever the filter changes rather than baked into the row.
        _maxEffort = _rows.Count == 0 ? 0 : _rows.Max(r => r.Effort);

        StateHasChanged();
    }

    private void Open(uint appId)
    {
        State.Select(appId);
        Navigation.NavigateTo($"game/{appId}");
    }

    private string EmptyTitle => _queue.Rows.Count == 0
        ? "Nothing left to rank"
        : "No games match those filters";

    private string EmptyBody => _queue.Rows.Count == 0
        ? "Every game with achievements in your library is at 100 %. Play something new, then sync."
        : "Widen the search or lower the minimum playtime.";
}
```

Create `SteamAchievements.UI/Queue/QueuePage.razor.css`:

```css
.list {
    display: flex;
    flex-direction: column;
    gap: 8px;
    padding: 18px 30px 40px;
}
```

- [ ] **Step 11: See it and exercise it**

Run: `dotnet run --project SteamAchievements.Preview` and open `http://localhost:5100`.

Expected:
- Thirteen rows (Celeste and Portal 2 hidden by the default "100 % complete: hidden"), Hollow Knight first with effort `4.2` and the line "1 left: one rare (2.1%)".
- Real cover art for every game.
- Clicking "Effort" flips the arrow to ↓ and puts Europa Universalis IV first.
- Typing "knight" leaves one row; clearing it restores thirteen.
- Selecting "Min. playtime: 20 h" drops nothing here; "5 h" and "1 h" likewise — every fixture game is above 20 h, which is the correct outcome, not a bug.
- Toggling "100 % complete" to "shown" brings Celeste and Portal 2 in, dimmed, with a grey bar.
- Hovering a row highlights it.

Then check `http://localhost:5100/?scenario=empty` renders "Nothing left to rank" — this needs the scenario wiring, added in Task 13. If it does not work yet, that is expected.

- [ ] **Step 12: Commit**

```bash
git add -A SteamAchievements.UI
git commit -m "feat: build the completion queue screen"
```

---

## Task 11: Virtualization and keyboard navigation

A real library holds around 1500 rows. Rendering them all would put roughly twenty thousand nodes in the DOM; the design doc calls for virtualization. The fixed row height that makes virtualization work also makes keyboard scrolling arithmetic instead of guesswork.

**Files:**
- Modify: `SteamAchievements.UI/Queue/QueuePage.razor` and `.razor.css`
- Modify: `SteamAchievements.UI/Queue/QueueToolbar.razor` (add an id for measurement)
- Modify: `SteamAchievements.UI/Layout/AppShell.razor` (add an id on the scroll container)
- Modify: `SteamAchievements.UI/wwwroot/queue-scroll.js`

**Interfaces:**
- Consumes: `QueueState.Select` (Task 10), `QueueFilter.Apply` (Task 10).
- Produces: `window.queueScroll.toIndex(index, rowHeight)`.

- [ ] **Step 1: Give the scroll container and the toolbar ids**

In `SteamAchievements.UI/Layout/AppShell.razor`, change the main element to `<main class="content" id="app-scroll">`.

In `SteamAchievements.UI/Queue/QueueToolbar.razor`, change the outer div to `<div class="toolbar" id="queue-toolbar">`.

- [ ] **Step 2: Write the scroll helper**

Replace `SteamAchievements.UI/wwwroot/queue-scroll.js`:

```javascript
// Scrolls the keyboard-selected queue row into view.
//
// scrollIntoView is not an option here: under virtualization the selected row
// may not be in the DOM at all. With a fixed row height the position is pure
// arithmetic, so nothing has to be rendered first.
window.queueScroll = {
    toIndex: function (index, rowHeight) {
        const scroller = document.getElementById('app-scroll');
        const list = document.getElementById('queue-list');
        if (!scroller || !list) {
            return;
        }

        // The toolbar is sticky and overlays the top of the scroll area, so a
        // row parked exactly at scrollTop would sit underneath it.
        const toolbar = document.getElementById('queue-toolbar');
        const overlay = toolbar ? toolbar.offsetHeight : 0;

        const top = list.offsetTop + index * rowHeight;
        const bottom = top + rowHeight;

        if (top - overlay < scroller.scrollTop) {
            scroller.scrollTop = top - overlay;
        } else if (bottom > scroller.scrollTop + scroller.clientHeight) {
            scroller.scrollTop = bottom - scroller.clientHeight;
        }
    }
};
```

- [ ] **Step 3: Virtualize the list and handle the keys**

Replace `SteamAchievements.UI/Queue/QueuePage.razor`:

```razor
@page "/"
@implements IDisposable
@inject ILibraryQuery Library
@inject IClock Clock
@inject QueueState State
@inject NavigationManager Navigation
@inject IJSRuntime Js

<PageTitle>Completion queue</PageTitle>

<QueueToolbar State="State" Shown="_rows.Count" Total="_queue.TotalGames" />

<div class="list" id="queue-list" tabindex="0" @ref="_list" @onkeydown="OnKeyDown">
    @if (_rows.Count == 0)
    {
        <EmptyState Title="@EmptyTitle" Body="@EmptyBody" />
    }
    else
    {
        <Virtualize Items="_rows" Context="row" ItemSize="RowHeight" OverscanCount="4">
            <div class="slot">
                <QueueRowCard Row="row"
                              BarPercent="QueueRowBuilder.EffortBarPercent(row.Effort, _maxEffort)"
                              Selected="State.SelectedAppId == row.AppId"
                              OnOpen="() => Open(row.AppId)"
                              OnSelect="() => State.Select(row.AppId)" />
            </div>
        </Virtualize>
    }
</div>

@code {
    /// <summary>
    /// Must match .slot in the stylesheet exactly: 96px cover + 12px padding
    /// top and bottom + 1px border each side + 8px gap. Virtualize positions
    /// rows from this number and the scroll helper computes offsets from it,
    /// so a mismatch shows up as drift rather than as an error.
    /// </summary>
    private const float RowHeight = 130;

    private QueueView _queue = new([], 0);
    private IReadOnlyList<QueueRow> _rows = [];
    private double _maxEffort;
    private ElementReference _list;
    private bool _focusPending = true;

    protected override void OnInitialized()
    {
        _queue = Library.GetQueue(Clock.Now);
        State.Changed += Refresh;
        Refresh();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Focus once so the arrow keys work without the user clicking first,
        // including when arriving back here from a game screen.
        if (_focusPending)
        {
            _focusPending = false;
            await _list.FocusAsync();
        }
    }

    public void Dispose() => State.Changed -= Refresh;

    private void Refresh()
    {
        _rows = QueueFilter.Apply(_queue.Rows, State.Criteria);
        _maxEffort = _rows.Count == 0 ? 0 : _rows.Max(r => r.Effort);
        StateHasChanged();
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        var current = Math.Max(0, IndexOfSelected());

        switch (e.Key)
        {
            case "ArrowDown":
                await MoveTo(Math.Min(_rows.Count - 1, current + 1));
                break;

            case "ArrowUp":
                await MoveTo(Math.Max(0, current - 1));
                break;

            case "Enter":
                Open(_rows[current].AppId);
                break;
        }
    }

    private int IndexOfSelected()
    {
        if (State.SelectedAppId is not { } id)
        {
            return -1;
        }

        // A plain loop rather than LINQ: this runs on every arrow keypress
        // over a list that can hold 1500 rows, and Select/FindIndex would
        // allocate a copy each time.
        for (var i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].AppId == id)
            {
                return i;
            }
        }

        return -1;
    }

    private async Task MoveTo(int index)
    {
        State.Select(_rows[index].AppId);
        await Js.InvokeVoidAsync("queueScroll.toIndex", index, RowHeight);
    }

    private void Open(uint appId)
    {
        State.Select(appId);
        Navigation.NavigateTo($"game/{appId}");
    }

    private string EmptyTitle => _queue.Rows.Count == 0
        ? "Nothing left to rank"
        : "No games match those filters";

    private string EmptyBody => _queue.Rows.Count == 0
        ? "Every game with achievements in your library is at 100 %. Play something new, then sync."
        : "Widen the search or lower the minimum playtime.";
}
```

Replace `SteamAchievements.UI/Queue/QueuePage.razor.css` — `Virtualize` positions its own children, so the gap moves from the flex container onto each slot:

```css
.list {
    padding: 18px 30px 40px;
    outline: none;
}

.slot {
    height: 130px;
    padding-bottom: 8px;
}
```

- [ ] **Step 4: Verify by hand**

Run: `dotnet run --project SteamAchievements.Preview` and open `http://localhost:5100`.

Expected:
- The list looks unchanged from Task 10 — same rows, same spacing, no visible gaps or overlap. Any drift here means `RowHeight` and `.slot` disagree.
- Pressing Down repeatedly walks the selection to the bottom and stops; Up walks back to the top and stops.
- Pressing Enter on a selected row navigates to `/game/<appid>` (which 404s until Task 12 — that is expected).
- With the browser window shortened so only three rows fit, holding Down scrolls the list to keep the selection visible, and the selected row never disappears under the sticky toolbar.

- [ ] **Step 5: Commit**

```bash
git add -A SteamAchievements.UI
git commit -m "feat: virtualize the queue and add keyboard navigation"
```

---

## Task 12: Game screen

**Files:**
- Create: `SteamAchievements.UI/Game/GamePage.razor` and `.razor.css`
- Create: `SteamAchievements.UI/Game/GameHeader.razor` and `.razor.css`
- Create: `SteamAchievements.UI/Game/StatCard.razor` and `.razor.css`
- Create: `SteamAchievements.UI/Game/RemainingRow.razor` and `.razor.css`
- Create: `SteamAchievements.UI/Game/UnlockedRow.razor` and `.razor.css`

**Interfaces:**
- Consumes: `ILibraryQuery.GetGame`, `GameDetailView`, `AchievementRow` (Tasks 4, 6), `ProgressBar`, `CoverImage`, `Notice` (Tasks 9, 10).
- Produces: the route `game/{AppId:int}`.

- [ ] **Step 1: Build `StatCard`**

Create `SteamAchievements.UI/Game/StatCard.razor`:

```razor
<div class="stat">
    <span class="value num" style="color: @ValueColor">@Value</span>
    <span class="micro">@Label</span>
    <span class="note">@Note</span>
</div>

@code {
    [Parameter, EditorRequired] public string Value { get; set; } = "";
    [Parameter, EditorRequired] public string Label { get; set; } = "";
    [Parameter] public string Note { get; set; } = "";
    [Parameter] public string ValueColor { get; set; } = "var(--text)";
}
```

Create `SteamAchievements.UI/Game/StatCard.razor.css`:

```css
.stat {
    display: flex;
    flex-direction: column;
    gap: 5px;
    min-width: 130px;
    padding: 14px 18px;
    border: 1px solid var(--border-subtle);
    border-radius: 10px;
    background: var(--bg-card);
}

.value { font-size: 22px; line-height: 1; }
.note { font-size: 12px; color: var(--text-muted); }
```

- [ ] **Step 2: Build `GameHeader`**

Create `SteamAchievements.UI/Game/GameHeader.razor`:

```razor
<div class="banner" style="background-image: url('@BannerUrl')">
    <button class="back" @onclick="OnBack">← Queue</button>
</div>

@code {
    [Parameter, EditorRequired] public uint AppId { get; set; }
    [Parameter] public EventCallback OnBack { get; set; }

    private string BannerUrl =>
        $"https://cdn.cloudflare.steamstatic.com/steam/apps/{AppId}/header.jpg";
}
```

Create `SteamAchievements.UI/Game/GameHeader.razor.css`:

```css
.banner {
    position: relative;
    height: 230px;
    /* The hatch shows through when header.jpg is missing, which is the same
       placeholder the queue covers fall back to. */
    background-color: #1d1b22;
    background-image: repeating-linear-gradient(135deg, #232028 0 10px, #1d1b22 10px 20px);
    background-size: cover;
    background-position: center;
}

.back {
    position: absolute;
    top: 18px;
    left: 24px;
    font: inherit;
    font-size: 12px;
    padding: 7px 12px;
    border-radius: 7px;
    border: 1px solid var(--border-strong);
    background: rgba(20, 19, 23, 0.6);
    color: var(--text-secondary);
    cursor: pointer;
}

.back:hover { color: var(--text); border-color: var(--border-hover); }
```

- [ ] **Step 3: Build the two achievement rows**

Create `SteamAchievements.UI/Game/RemainingRow.razor`:

```razor
<div class="row">
    <div class="icon-slot">
        <CoverImage Url="@Row.IconUrl" FallbackLabel="" />
    </div>

    <div class="text">
        <span class="name @(Row.Hidden ? "hidden" : "")">@Row.Name</span>
        <span class="desc">@Row.Description</span>
    </div>

    <div class="rarity">
        <span class="num pct">@Row.PercentText</span>
        <ProgressBar Percent="Row.RarityBarPercent" Color="var(--accent-dim)" />
    </div>

    <div class="cost">
        <span class="num value">@Row.CostText</span>
        <span class="micro">cost</span>
    </div>
</div>

@code {
    [Parameter, EditorRequired] public AchievementRow Row { get; set; } = default!;
}
```

Create `SteamAchievements.UI/Game/RemainingRow.razor.css`:

```css
.row {
    display: grid;
    grid-template-columns: 48px 1fr 110px 74px;
    gap: 16px;
    align-items: center;
    padding: 12px 14px;
    border: 1px solid var(--border-subtle);
    border-radius: 9px;
    background: var(--bg-card);
}

.icon-slot { height: 48px; }
.text { display: flex; flex-direction: column; gap: 3px; min-width: 0; }
.name { font-size: 14px; font-weight: 600; }
.name.hidden { color: #8d8579; }

.desc {
    font-size: 12px;
    color: var(--text-muted);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}

.rarity { display: flex; flex-direction: column; gap: 5px; }
.pct { font-size: 12px; color: var(--text-secondary); }

.cost { display: flex; flex-direction: column; gap: 3px; text-align: right; }
.cost .value { font-size: 14px; color: var(--accent); }
.cost .micro { text-align: right; }
```

Create `SteamAchievements.UI/Game/UnlockedRow.razor`:

```razor
<div class="row">
    <div class="icon-slot">
        <CoverImage Url="@Row.IconUrl" FallbackLabel="" />
    </div>
    <span class="name">@Row.Name</span>
    <span class="num pct">@(Row.GlobalPercent is { } p ? Formatting.Percent(p) : "—")</span>
    <span class="num date">@Row.UnlockedDateText</span>
</div>

@code {
    [Parameter, EditorRequired] public AchievementRow Row { get; set; } = default!;
}
```

Create `SteamAchievements.UI/Game/UnlockedRow.razor.css`:

```css
.row {
    display: grid;
    grid-template-columns: 36px 1fr auto auto;
    gap: 14px;
    align-items: center;
    padding: 9px 4px;
    border-bottom: 1px solid var(--border-hairline);
}

.icon-slot { height: 36px; }

.name {
    font-size: 13px;
    color: var(--text-secondary);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}

.pct { font-size: 12px; color: var(--text-dim); }
.date { font-size: 12px; color: var(--text-dim); min-width: 96px; text-align: right; }
```

- [ ] **Step 4: Assemble the page**

Create `SteamAchievements.UI/Game/GamePage.razor`:

```razor
@page "/game/{AppId:int}"
@inject ILibraryQuery Library
@inject IClock Clock
@inject NavigationManager Navigation

@if (_game is null)
{
    <div class="missing">
        <EmptyState Title="That game is not in your library"
                    Body="It may have been removed from your account, or the link is stale." />
    </div>
}
else
{
    <PageTitle>@_game.Name</PageTitle>

    <GameHeader AppId="_game.AppId" OnBack="Back" />

    <div class="body">
        <div class="summary">
            <div class="titles">
                <h1>@_game.Name</h1>
                <div class="meta">@_game.PlaytimeText played · last played @_game.LastPlayedText</div>
                <ProgressBar Percent="_game.CompletionPercent" Height="4" MaxWidth="460px" />
                <div class="num progress">
                    @_game.Unlocked of @_game.Total unlocked · @_game.CompletionPercent%
                </div>
            </div>

            <div class="stats">
                <StatCard Value="@_game.EffortText" Label="remaining effort"
                          Note="@_game.EffortLabel" ValueColor="var(--accent)" />
                <StatCard Value="@_game.Remaining.ToString()" Label="left"
                          Note="@($"rarest {_game.RarestText}")" />
            </div>
        </div>

        @if (_game.RarestText == "unknown" && _game.Remaining > 0)
        {
            <Notice Title="Rarity data unavailable">
                <Body>
                    Steam has no global percentages for this game yet, so every remaining
                    achievement counts as one unit and rarity reads
                    <span class="num">unknown</span> — not zero.
                </Body>
            </Notice>
        }

        <section>
            <div class="section-head">
                <h2>Remaining</h2>
                <span class="num hint">cheapest first</span>
            </div>
            @if (_game.RemainingAchievements.Count == 0)
            {
                <EmptyState Title="Nothing left here"
                            Body="Every achievement in this game is unlocked." />
            }
            else
            {
                <div class="rows">
                    @foreach (var row in _game.RemainingAchievements)
                    {
                        <RemainingRow Row="row" />
                    }
                </div>
            }
        </section>

        <section>
            <div class="section-head">
                <h2>Unlocked</h2>
                <span class="num hint">most recent first</span>
            </div>
            <div>
                @foreach (var row in _game.UnlockedAchievements)
                {
                    <UnlockedRow Row="row" />
                }
            </div>
        </section>
    </div>
}

@code {
    [Parameter] public int AppId { get; set; }

    private GameDetailView? _game;

    // Re-read on every parameter change, not just on initialization: clicking
    // another game while already on this route reuses the component instance.
    protected override void OnParametersSet() => _game = Library.GetGame((uint)AppId, Clock.Now);

    private void Back() => Navigation.NavigateTo("/");
}
```

Create `SteamAchievements.UI/Game/GamePage.razor.css`:

```css
.body {
    display: flex;
    flex-direction: column;
    gap: 26px;
    padding: 24px 30px 46px;
}

.missing { padding: 40px 30px; }

.summary { display: flex; gap: 26px; align-items: flex-end; flex-wrap: wrap; }

.titles { display: flex; flex-direction: column; gap: 8px; flex: 1; min-width: 260px; }
.titles h1 { margin: 0; font-size: 26px; font-weight: 600; letter-spacing: -0.025em; }
.meta { font-size: 13px; color: var(--text-muted); }
.progress { font-size: 12px; color: var(--text-secondary); }

.stats { display: flex; gap: 10px; }

section { display: flex; flex-direction: column; gap: 10px; }
.section-head { display: flex; align-items: baseline; gap: 10px; }
.section-head h2 { margin: 0; font-size: 14px; font-weight: 600; letter-spacing: 0.02em; }
.hint { font-size: 11px; color: var(--text-dim); }

.rows { display: flex; flex-direction: column; gap: 8px; }
```

- [ ] **Step 5: See it**

Run: `dotnet run --project SteamAchievements.Preview`, open `http://localhost:5100`, and click Hollow Knight.

Expected:
- The real `header.jpg` banner with a "← Queue" button over it.
- "Hollow Knight", "84 h played · last played 3 days ago", the progress bar, and "60 of 63 unlocked · 95%".
- Two stat cards: `4.2` / "remaining effort" / "an evening", and `3` / "left" / "rarest 2.1%".
- Remaining ordered cheapest first, ending with the hidden one, which reads "Hidden achievement" in a dimmed colour with the explanatory description.
- Achievement icons all fall back to the hatch placeholder — the fixture URLs do not resolve on purpose, which is exactly the case worth seeing.
- "← Queue" returns to the queue with Hollow Knight still selected and the sort unchanged.
- Opening `http://localhost:5100/game/435150` (Divinity) shows the "Rarity data unavailable" notice.
- Opening `http://localhost:5100/game/1` shows "That game is not in your library".

- [ ] **Step 6: Commit**

```bash
git add -A SteamAchievements.UI
git commit -m "feat: build the game detail screen"
```

---

## Task 13: Sync screen and scenario wiring

The sync screen renders completely, but nothing behind it runs: connecting it to `SyncOrchestrator` is the next spec. The seam it will connect through is defined here, with an idle implementation in production and a fixture one in preview — which is also what finally makes the error states visible.

**Files:**
- Create: `SteamAchievements.UI/State/SyncStatusView.cs`
- Create: `SteamAchievements.UI/Sync/SyncPage.razor` and `.razor.css`
- Create: `SteamAchievements.UI/Sync/SyncProgressCard.razor` and `.razor.css`
- Create: `SteamAchievements.UI/Sync/SyncHistoryRow.razor` and `.razor.css`
- Create: `SteamAchievements.Preview/Fixtures/FixtureSyncPresenter.cs`
- Create: `SteamAchievements.Preview/Components/ScenarioScope.razor`
- Modify: `SteamAchievements.Preview/Components/Routes.razor`, `Program.cs`

**Interfaces:**
- Consumes: `ILibraryQuery.GetSyncHistory` (Task 6), `Notice` (Task 9), `ProgressBar` (Task 10).
- Produces: `SyncPhase` enum (`Idle`, `Running`, `Paused`, `CircuitOpen`); `SyncStatusView(SyncPhase Phase, int Completed, int Total, string CurrentGame, string EtaText, string RateText, string? AlertTitle, string? AlertBody)`; `ISyncPresenter` with `SyncStatusView Status { get; }`; `IdleSyncPresenter`; the route `sync`.

- [ ] **Step 1: Define the sync seam**

Create `SteamAchievements.UI/State/SyncStatusView.cs`:

```csharp
namespace SteamAchievements.UI.State;

public enum SyncPhase
{
    Idle,
    Running,
    Paused,
    CircuitOpen,
}

public sealed record SyncStatusView(
    SyncPhase Phase,
    int       Completed,
    int       Total,
    string    CurrentGame,
    string    EtaText,
    string    RateText,
    string?   AlertTitle,
    string?   AlertBody)
{
    public static SyncStatusView Idle { get; } =
        new(SyncPhase.Idle, 0, 0, "", "", "", null, null);

    public int Percent => Total == 0 ? 0 : (int)Math.Round(100.0 * Completed / Total);
}

/// <summary>
/// What the sync screen renders. Deliberately a seam rather than a direct
/// dependency on SyncOrchestrator: wiring the two together — real progress,
/// pause, cancel, rate and ETA — is the next spec's work, and this lets the
/// screen be finished and verified before that exists.
/// </summary>
public interface ISyncPresenter
{
    SyncStatusView Status { get; }
}

/// <summary>The production implementation until the sync spec replaces it.</summary>
public sealed class IdleSyncPresenter : ISyncPresenter
{
    public SyncStatusView Status => SyncStatusView.Idle;
}
```

- [ ] **Step 2: Build the progress card and the history row**

Create `SteamAchievements.UI/Sync/SyncProgressCard.razor`:

```razor
<div class="card">
    <div class="head">
        <span class="title">@Title</span>
        @if (Status.Total > 0)
        {
            <span class="num counter">
                @Formatting.Number(Status.Completed) / @Formatting.Number(Status.Total)
            </span>
        }
    </div>

    <ProgressBar Percent="Status.Percent" Height="6" />

    <div class="detail num">
        <span>@(Status.CurrentGame.Length == 0 ? "idle" : $"now: {Status.CurrentGame}")</span>
        <span>@Status.EtaText@(Status.RateText.Length == 0 ? "" : $" · {Status.RateText}")</span>
    </div>

    <div class="actions">
        <button class="primary" disabled="@Disabled">
            @(Status.Phase == SyncPhase.Paused ? "Resume" : "Pause")
        </button>
        <button class="ghost" disabled="@Disabled">Cancel</button>
        <span class="spacer"></span>
        <span class="note">Progress is saved — closing the app is safe</span>
    </div>
</div>

@code {
    [Parameter, EditorRequired] public SyncStatusView Status { get; set; } = default!;

    private bool Disabled => Status.Phase == SyncPhase.Idle;

    private string Title => Status.Phase switch
    {
        SyncPhase.Running => "Full sync in progress",
        SyncPhase.Paused => "Sync paused",
        SyncPhase.CircuitOpen => "Sync waiting to retry",
        _ => "No sync running",
    };
}
```

The Pause and Cancel buttons are disabled while idle and do nothing when enabled — the handlers arrive with the sync spec. They are rendered now so the layout is settled and reviewable.

Create `SteamAchievements.UI/Sync/SyncProgressCard.razor.css`:

```css
.card {
    display: flex;
    flex-direction: column;
    gap: 16px;
    padding: 20px;
    border: 1px solid var(--border);
    border-radius: 11px;
    background: var(--bg-card);
}

.head { display: flex; align-items: baseline; justify-content: space-between; gap: 14px; }
.title { font-size: 14px; font-weight: 600; }
.counter { font-size: 13px; color: var(--accent); }

.detail {
    display: flex;
    justify-content: space-between;
    gap: 14px;
    flex-wrap: wrap;
    font-size: 11px;
    color: var(--text-dim);
}

.actions { display: flex; gap: 9px; align-items: center; }
.spacer { flex: 1; }
.note { font-size: 12px; color: var(--text-dim); }

.primary, .ghost {
    font: inherit;
    font-size: 13px;
    padding: 9px 15px;
    border-radius: 8px;
    cursor: pointer;
}

.primary { border: 1px solid var(--border-strong); background: var(--bg-selected); color: var(--text); }
.primary:hover:enabled { border-color: var(--border-hover); }

.ghost { border: 1px solid var(--border); background: transparent; color: var(--text-muted); }
.ghost:hover:enabled { color: var(--text); }

.primary:disabled, .ghost:disabled { opacity: 0.45; cursor: default; }
```

Create `SteamAchievements.UI/Sync/SyncHistoryRow.razor`:

```razor
<div class="row">
    <span class="num when">@Run.WhenText</span>
    <span class="what @(Run.Failed ? "failed" : "")">@Run.WhatText</span>
    <span class="num dur">@Run.DurationText</span>
</div>

@code {
    [Parameter, EditorRequired] public SyncRunView Run { get; set; } = default!;
}
```

Create `SteamAchievements.UI/Sync/SyncHistoryRow.razor.css`:

```css
.row {
    display: grid;
    grid-template-columns: 150px 1fr auto;
    gap: 14px;
    align-items: center;
    padding: 10px 2px;
    border-bottom: 1px solid var(--border-hairline);
    font-size: 13px;
}

.when { font-size: 12px; color: var(--text-muted); }
.what { color: var(--text-secondary); }
.what.failed { color: var(--danger-text); }
.dur { font-size: 12px; color: var(--text-dim); }
```

- [ ] **Step 3: Assemble the page**

Create `SteamAchievements.UI/Sync/SyncPage.razor`:

```razor
@page "/sync"
@inject ILibraryQuery Library
@inject IClock Clock
@inject ISyncPresenter Sync
@inject NavigationManager Navigation

<PageTitle>Sync</PageTitle>

<div class="page">
    <div class="intro">
        <h1>Sync</h1>
        <p>Steam allows about five requests per second. Games whose playtime has not
           changed are skipped entirely.</p>
    </div>

    <SyncProgressCard Status="_status" />

    @if (_status.AlertTitle is not null)
    {
        <Notice Severity="NoticeSeverity.Warning" Title="@_status.AlertTitle"
                ActionLabel="Retry now">
            <Body>@_status.AlertBody</Body>
        </Notice>
    }

    @if (_summary.GameCount == 0)
    {
        <Notice Severity="NoticeSeverity.Danger"
                Title="Steam rejected the API key"
                ActionLabel="Open settings"
                OnAction="OpenSettings">
            <Body>
                HTTP 401. The key was revoked or mistyped — retrying will not help.
                Replace it in Settings.
            </Body>
        </Notice>

        <Notice Severity="NoticeSeverity.Warning" Title="Your game details are private">
            <Body>
                Steam returns an empty library even to you until
                <strong>Game details</strong> is set to Public in your privacy settings.
            </Body>
        </Notice>
    }

    <div class="history">
        <div class="micro">History</div>
        @if (_history.Count == 0)
        {
            <p class="empty">No syncs recorded yet.</p>
        }
        else
        {
            @foreach (var run in _history)
            {
                <SyncHistoryRow Run="run" />
            }
        }
    </div>
</div>

@code {
    private SyncStatusView _status = SyncStatusView.Idle;
    private LibrarySummary _summary = new(0, 0, "", "");
    private IReadOnlyList<SyncRunView> _history = [];

    protected override void OnInitialized()
    {
        _status = Sync.Status;
        _summary = Library.GetSummary(Clock.Now);
        _history = Library.GetSyncHistory(20, Clock.Now);
    }

    private void OpenSettings() => Navigation.NavigateTo("settings");
}
```

An empty library is the only signal available here for the "rejected key" and "private profile" states until the sync engine reports its own errors, so both notices are shown together and the wording leaves the choice to the reader. Distinguishing them properly needs `sync_state.last_error`, which the sync spec wires up.

Create `SteamAchievements.UI/Sync/SyncPage.razor.css`:

```css
.page {
    display: flex;
    flex-direction: column;
    gap: 22px;
    padding: 30px;
    max-width: 760px;
}

.intro { display: flex; flex-direction: column; gap: 6px; }
.intro h1 { margin: 0; font-size: 22px; font-weight: 600; letter-spacing: -0.02em; }
.intro p { margin: 0; font-size: 13px; color: var(--text-muted); text-wrap: pretty; }

.history { display: flex; flex-direction: column; gap: 9px; }
.empty { margin: 0; font-size: 13px; color: var(--text-dim); }
```

- [ ] **Step 4: Wire the preview scenarios**

Create `SteamAchievements.Preview/Fixtures/FixtureSyncPresenter.cs`:

```csharp
using SteamAchievements.UI.State;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>Shows the sync screen under each state without running a sync.</summary>
public sealed class FixtureSyncPresenter(FixtureLibraryQuery library) : ISyncPresenter
{
    public SyncStatusView Status => library.Scenario switch
    {
        Scenario.Empty or Scenario.InvalidKey or Scenario.PrivateProfile => SyncStatusView.Idle,

        Scenario.OtherAccount => new SyncStatusView(
            SyncPhase.CircuitOpen, 412, 1482, "Divinity: Original Sin 2 — schema",
            "~6 min left", "4.8 req/s",
            "Paused after five consecutive failures",
            "Steam returned 429 five times in a row. Waiting 8 s before the next attempt."),

        _ => new SyncStatusView(
            SyncPhase.Running, 412, 1482, "Divinity: Original Sin 2 — schema",
            "~6 min left", "4.8 req/s", null, null),
    };
}
```

Create `SteamAchievements.Preview/Components/ScenarioScope.razor` — preview-only plumbing, not screen markup:

```razor
@using Microsoft.AspNetCore.WebUtilities
@using SteamAchievements.Preview.Fixtures
@implements IDisposable
@inject FixtureLibraryQuery Library
@inject NavigationManager Navigation

@ChildContent

@code {
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized()
    {
        Navigation.LocationChanged += OnLocationChanged;
        Apply();
    }

    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => Apply();

    private void Apply()
    {
        var query = QueryHelpers.ParseQuery(new Uri(Navigation.Uri).Query);

        Library.Scenario = query.TryGetValue("scenario", out var value)
            && Enum.TryParse<Scenario>(value.ToString().Replace("-", ""), ignoreCase: true, out var scenario)
                ? scenario
                : Scenario.Normal;
    }
}
```

In `SteamAchievements.Preview/Components/Routes.razor`, wrap the router:

```razor
@using SteamAchievements.UI.Layout
@using SteamAchievements.Preview.Components

<ScenarioScope>
    <Router AppAssembly="typeof(Program).Assembly"
            AdditionalAssemblies="new[] { typeof(AppShell).Assembly }">
        <Found Context="routeData">
            <RouteView RouteData="routeData" DefaultLayout="typeof(AppShell)" />
            <FocusOnNavigate RouteData="routeData" Selector="h1" />
        </Found>
    </Router>
</ScenarioScope>
```

In `SteamAchievements.Preview/Program.cs`, register the presenter next to the other scoped services:

```csharp
builder.Services.AddScoped<ISyncPresenter, FixtureSyncPresenter>();
```

- [ ] **Step 5: See every state**

Run: `dotnet run --project SteamAchievements.Preview` and walk through these URLs.

| URL | Expected |
|---|---|
| `/sync` | "Full sync in progress", `412 / 1 482`, a 28% bar, "now: Divinity: Original Sin 2 — schema", "~6 min left · 4.8 req/s", four history rows |
| `/sync?scenario=other-account` | The same card plus the amber "Paused after five consecutive failures" notice with a "Retry now" button |
| `/sync?scenario=invalid-key` | "No sync running" with disabled buttons, the red "Steam rejected the API key" notice whose button opens Settings, the amber privacy notice, and "No syncs recorded yet." |
| `/?scenario=empty` | "Nothing left to rank" |
| `/?scenario=rarity-unknown` | One row, Divinity, reading "39 left, rarity unknown for six of them" |

- [ ] **Step 6: Commit**

```bash
git add -A SteamAchievements.UI SteamAchievements.Preview
git commit -m "feat: build the sync screen and wire preview scenarios"
```

---

## Task 14: Settings screen

The only screen with a control that genuinely works: the accent picker writes through `IUserPreferences` and is read back by the shell.

**Files:**
- Create: `SteamAchievements.UI/Settings/SettingsPage.razor` and `.razor.css`
- Create: `SteamAchievements.UI/Settings/AccentPicker.razor` and `.razor.css`
- Create: `SteamAchievements.UI/Settings/CacheRow.razor` and `.razor.css`

**Interfaces:**
- Consumes: `IUserPreferences` (Task 5), `SyncOptions.Default` from `SteamAchievements.Core.Sync`, `Notice` (Task 9).
- Produces: the route `settings`; `AccentPicker` with `Selected` and `OnSelect`.

- [ ] **Step 1: Build `AccentPicker`**

Create `SteamAchievements.UI/Settings/AccentPicker.razor`:

```razor
<div class="picker">
    @foreach (var (value, name) in Accents)
    {
        <button class="swatch @(Selected == value ? "on" : "")"
                style="background: @value"
                title="@name"
                aria-label="@name"
                @onclick="() => OnSelect.InvokeAsync(value)">
        </button>
    }
</div>

@code {
    [Parameter, EditorRequired] public string Selected { get; set; } = "";
    [Parameter] public EventCallback<string> OnSelect { get; set; }

    /// <summary>The four the mockup offers.</summary>
    public static readonly (string Value, string Name)[] Accents =
    [
        ("#e0a355", "Amber"),
        ("#8fb3c9", "Blue"),
        ("#c98f7a", "Terracotta"),
        ("#a8b58c", "Olive"),
    ];
}
```

Create `SteamAchievements.UI/Settings/AccentPicker.razor.css`:

```css
.picker { display: flex; gap: 8px; }

.swatch {
    width: 30px;
    height: 30px;
    border-radius: 8px;
    border: 2px solid transparent;
    cursor: pointer;
}

.swatch.on { border-color: var(--text); }
.swatch:hover { border-color: var(--border-hover); }
```

- [ ] **Step 2: Build `CacheRow`**

Create `SteamAchievements.UI/Settings/CacheRow.razor`:

```razor
<div class="row">
    <div class="text">
        <span class="name">@Name</span>
        <span class="note">@Note</span>
    </div>
    <span class="num value">@Value</span>
</div>

@code {
    [Parameter, EditorRequired] public string Name { get; set; } = "";
    [Parameter, EditorRequired] public string Note { get; set; } = "";
    [Parameter, EditorRequired] public string Value { get; set; } = "";
}
```

Create `SteamAchievements.UI/Settings/CacheRow.razor.css`:

```css
.row {
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 16px;
    padding: 13px 15px;
    border: 1px solid var(--border-subtle);
    border-radius: 9px;
    background: var(--bg-card);
}

.text { display: flex; flex-direction: column; gap: 3px; }
.name { font-size: 13px; }
.note { font-size: 12px; color: var(--text-dim); }
.value { font-size: 13px; color: var(--text-secondary); }
```

- [ ] **Step 3: Assemble the page**

Create `SteamAchievements.UI/Settings/SettingsPage.razor`:

```razor
@page "/settings"
@using SteamAchievements.Core.Sync
@inject IUserPreferences Preferences
@inject ILibraryQuery Library
@inject IClock Clock

<PageTitle>Settings</PageTitle>

<div class="page">
    <h1>Settings</h1>

    <div class="account">
        <div class="avatar"></div>
        <div class="who">
            <span class="name">Not signed in yet</span>
            <span class="num id">Detected from loginusers.vdf during onboarding</span>
        </div>
        <span class="spacer"></span>
        <button class="secondary" disabled>Change account</button>
    </div>

    <div class="block">
        <div class="label">Steam Web API key</div>
        <div class="key-row">
            <div class="num key">Not stored yet</div>
            <button class="secondary" disabled>Replace</button>
        </div>
        <div class="hint">Encrypted with Windows DPAPI for this user account. Never stored in plaintext.</div>
    </div>

    <div class="block">
        <div class="label">Appearance</div>
        <AccentPicker Selected="@Accent" OnSelect="Choose" />
        <div class="hint">Changes the accent used for effort figures, progress and selection.</div>
    </div>

    <div class="block">
        <div class="label">Cache</div>
        <CacheRow Name="Achievement schema" Note="Rarely changes after release"
                  Value="@($"{SyncOptions.Default.SchemaTtl.TotalDays:0} days")" />
        <CacheRow Name="Global rarity percentages" Note="Drifts slowly as more people play"
                  Value="@($"{SyncOptions.Default.GlobalTtl.TotalDays:0} days")" />
        <div class="hint">These are the sync engine's own decisions and are shown, not edited.</div>
    </div>

    <div class="block">
        <div class="label">Local data</div>
        <Notice Severity="NoticeSeverity.Danger" Title="Reset database" ActionLabel="Reset">
            <Body>
                Deletes @Formatting.Number(_summary.AchievementCount) cached achievements and all
                snapshots. The next sync starts from nothing.
            </Body>
        </Notice>
    </div>
</div>

@code {
    private LibrarySummary _summary = new(0, 0, "", "");

    private string Accent => Preferences.Accent ?? AppShell.DefaultAccent;

    protected override void OnInitialized() => _summary = Library.GetSummary(Clock.Now);

    private void Choose(string accent) => Preferences.SetAccent(accent);
}
```

The account block, the key field, Replace, Change account and Reset are rendered and inert: every one of them needs Windows APIs or the sync engine, and both are the next spec's work. They are built now so the screen is complete and reviewable rather than half-drawn.

Create `SteamAchievements.UI/Settings/SettingsPage.razor.css`:

```css
.page {
    display: flex;
    flex-direction: column;
    gap: 24px;
    padding: 30px;
    max-width: 720px;
}

.page h1 { margin: 0; font-size: 22px; font-weight: 600; letter-spacing: -0.02em; }

.account {
    display: flex;
    gap: 16px;
    align-items: center;
    padding: 18px;
    border: 1px solid var(--border-subtle);
    border-radius: 11px;
    background: var(--bg-card);
}

.avatar {
    width: 56px;
    height: 56px;
    border-radius: 8px;
    background: var(--placeholder);
    flex: none;
}

.who { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
.who .name { font-size: 15px; font-weight: 600; }
.who .id { font-size: 12px; color: var(--text-dim); }
.spacer { flex: 1; }

.block { display: flex; flex-direction: column; gap: 10px; }
.label { font-size: 14px; font-weight: 600; }
.hint { font-size: 12px; color: var(--text-dim); }

.key-row { display: flex; gap: 9px; align-items: center; flex-wrap: wrap; }

.key {
    flex: 1;
    min-width: 240px;
    font-size: 13px;
    padding: 11px 13px;
    border-radius: 8px;
    border: 1px solid var(--border);
    background: var(--bg-raised);
    color: var(--text-secondary);
}

.secondary {
    font: inherit;
    font-size: 13px;
    padding: 11px 14px;
    border-radius: 8px;
    border: 1px solid var(--border);
    background: var(--bg-selected);
    color: var(--text);
    cursor: pointer;
}

.secondary:disabled { opacity: 0.45; cursor: default; }
```

- [ ] **Step 4: Verify the accent actually persists through the seam**

Run: `dotnet run --project SteamAchievements.Preview` and open `http://localhost:5100/settings`.

Expected: clicking the blue swatch immediately turns the selected navigation dot, the effort figures on the queue and the progress bars blue. Navigating to the queue and back keeps it blue. Reloading the page resets it to amber, because the preview stores preferences in memory by design — the SQLite implementation is already tested in Task 5.

If the colour changes on Settings but not on the queue, the shell is not re-rendering: `AppShell` reads `Preferences.Accent` during render, so a `StateHasChanged` on the layout is needed. Fix it by having `SettingsPage` call `StateHasChanged` on itself and confirming the layout re-renders on navigation; if it still lags, raise an event from `QueueState` — do not duplicate the accent into a second field.

- [ ] **Step 5: Commit**

```bash
git add -A SteamAchievements.UI
git commit -m "feat: build the settings screen with a working accent picker"
```

---

## Task 15: Onboarding screen

Three steps, rendered with no logic behind them — detecting the account, watching the clipboard and running the first sync all need Windows APIs and the sync engine. The screen is built now so the copy and layout are settled before that work starts.

**Files:**
- Create: `SteamAchievements.UI/Onboarding/OnboardingPage.razor` and `.razor.css`
- Create: `SteamAchievements.UI/Onboarding/StepIndicator.razor` and `.razor.css`

**Interfaces:**
- Consumes: nothing beyond the tokens.
- Produces: the route `onboarding`.

- [ ] **Step 1: Build `StepIndicator`**

Create `SteamAchievements.UI/Onboarding/StepIndicator.razor`:

```razor
<div class="steps micro">
    @for (var i = 0; i < Labels.Length; i++)
    {
        if (i > 0)
        {
            <span>—</span>
        }
        <span class="@(i < Current ? "done" : "")">@(i + 1) @Labels[i]</span>
    }
</div>

@code {
    [Parameter, EditorRequired] public string[] Labels { get; set; } = [];

    /// <summary>How many steps are reached; those before it are shown in the accent.</summary>
    [Parameter] public int Current { get; set; }
}
```

Create `SteamAchievements.UI/Onboarding/StepIndicator.razor.css`:

```css
.steps { display: flex; gap: 7px; align-items: center; letter-spacing: 0.1em; }
.done { color: var(--accent); }
```

- [ ] **Step 2: Assemble the page**

Create `SteamAchievements.UI/Onboarding/OnboardingPage.razor`:

```razor
@page "/onboarding"

<PageTitle>Getting started</PageTitle>

<div class="page">
    <StepIndicator Labels="@(new[] { "account", "key", "first sync" })" Current="2" />

    <section class="card">
        <div class="head">
            <span class="title">Is this you?</span>
            <span class="sub">Found in your local Steam installation — nothing was sent anywhere.</span>
        </div>

        <div class="account">
            <div class="avatar"></div>
            @* Placeholder values. The mockup shows a real account here; a real
               SteamID64 must never be committed (CLAUDE.md), and these are
               replaced by ISteamPathProvider in the Windows spec anyway. *@
            <div class="who">
                <span class="name">Your Steam account</span>
                <span class="num id">76561190000000000</span>
            </div>
            <span class="spacer"></span>
            <button class="primary" disabled>Yes, continue</button>
        </div>

        <button class="link" disabled>Enter a SteamID64 or profile URL instead</button>
    </section>

    <section class="card key">
        <div class="head">
            <span class="title">Paste your Steam Web API key</span>
            <span class="sub bright">
                The button opens Steam's key page — you are already signed in there. Copy the
                key and this app picks it up from the clipboard on its own.
            </span>
        </div>

        <div class="key-row">
            <button class="primary" disabled>Open key page</button>
            <div class="watching num">
                <span class="pulse"></span>watching clipboard for 32 hex characters
            </div>
        </div>

        <div class="sub">
            Steam only issues keys to accounts with at least $5 in purchases. If the page
            refuses, that is why — not a problem with this app.
        </div>
    </section>

    <section class="card dim">
        <span class="title">First sync</span>
        <span class="sub">
            Roughly 9 minutes for 1 500 games, limited by Steam's rate limits. Later syncs
            take seconds.
        </span>
    </section>
</div>
```

The account name and SteamID64 shown here are deliberately fake. The mockup shows a real-looking account, and `CLAUDE.md` forbids committing real `steamid` values — so the screen carries an obviously-invalid placeholder until the Windows spec supplies `ISteamPathProvider` and the profile lookup. The shape of the screen is what is being settled now.

Create `SteamAchievements.UI/Onboarding/OnboardingPage.razor.css`:

```css
.page {
    display: flex;
    flex-direction: column;
    gap: 18px;
    padding: 36px 30px 46px;
    max-width: 660px;
    margin: 0 auto;
}

.card {
    display: flex;
    flex-direction: column;
    gap: 16px;
    padding: 22px;
    border: 1px solid var(--border-subtle);
    border-radius: 11px;
    background: var(--bg-card);
}

.card.key { border-color: var(--warn-border); background: #1c1811; }
.card.dim { opacity: 0.55; gap: 12px; }

.head { display: flex; flex-direction: column; gap: 5px; }
.title { font-size: 17px; font-weight: 600; letter-spacing: -0.01em; }
.sub { font-size: 13px; color: var(--text-muted); text-wrap: pretty; }
.sub.bright { color: var(--text-secondary); }

.account {
    display: flex;
    gap: 14px;
    align-items: center;
    padding: 14px;
    border: 1px solid var(--border);
    border-radius: 9px;
    background: var(--bg-raised);
}

.avatar { width: 52px; height: 52px; border-radius: 8px; background: var(--placeholder); flex: none; }
.who { display: flex; flex-direction: column; gap: 3px; }
.who .name { font-size: 15px; font-weight: 600; }
.who .id { font-size: 12px; color: var(--text-dim); }
.spacer { flex: 1; }

.primary {
    font: inherit;
    font-size: 13px;
    font-weight: 600;
    padding: 11px 16px;
    border-radius: 8px;
    border: 0;
    background: var(--accent);
    color: var(--on-accent);
    cursor: pointer;
}

.primary:disabled { opacity: 0.55; cursor: default; }

.link {
    align-self: flex-start;
    font: inherit;
    font-size: 12px;
    padding: 0;
    border: 0;
    background: transparent;
    color: var(--text-muted);
    text-decoration: underline;
    cursor: pointer;
}

.key-row { display: flex; gap: 10px; flex-wrap: wrap; }

.watching {
    display: flex;
    align-items: center;
    gap: 10px;
    flex: 1;
    min-width: 220px;
    font-size: 13px;
    padding: 11px 13px;
    border-radius: 8px;
    border: 1px dashed var(--warn-border);
    background: var(--warn-bg);
    color: #8d8579;
}

.pulse { width: 7px; height: 7px; border-radius: 7px; background: var(--accent); flex: none; }
```

- [ ] **Step 3: See it**

Run: `dotnet run --project SteamAchievements.Preview` and open `http://localhost:5100/onboarding`.

Expected: a 660px column centred in the content area; "1 account — 2 key — 3 first sync" with the first two in amber; three cards, the middle one amber-tinted with a dashed clipboard field, the last one dimmed to 55%.

- [ ] **Step 4: Commit**

```bash
git add -A SteamAchievements.UI
git commit -m "feat: build the onboarding screen"
```

---

## Task 16: CI, documentation and final verification

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `CLAUDE.md`
- Modify: `docs/superpowers/specs/2026-07-26-ui-screens-design.md` (divergence record)

**Interfaces:**
- Consumes: everything.
- Produces: nothing further.

- [ ] **Step 1: Build the UI on the ubuntu job**

Today a broken Razor component is caught only by the Windows job, three minutes later. In `.github/workflows/ci.yml`, add two steps to the `test` job after the existing `dotnet test` line:

```yaml
      - name: Build the component library
        run: dotnet build SteamAchievements.UI --configuration Release
      - name: Build the preview host
        run: dotnet build SteamAchievements.Preview --configuration Release
```

The `build-windows` job needs no change: its publish step already names `SteamAchievements.Windows` explicitly, so the preview host cannot leak into the single-file check.

- [ ] **Step 2: Verify CI locally as far as macOS allows**

```bash
dotnet test SteamAchievements.Core.Tests --configuration Release
dotnet build SteamAchievements.UI --configuration Release
dotnet build SteamAchievements.Preview --configuration Release
dotnet format SteamAchievements.Core
dotnet format SteamAchievements.UI
```

Expected: tests PASS with no failures; both builds succeed with no warnings; `dotnet format` reports no changes or only whitespace.

- [ ] **Step 3: Walk every screen once more**

Run: `dotnet run --project SteamAchievements.Preview` and confirm each of these in one sitting:

| URL | Confirm |
|---|---|
| `/` | 13 rows, Hollow Knight first, real covers, "1 left: one rare (2.1%)" |
| `/` | Sort buttons flip direction; search narrows; playtime filter applies; complete-toggle shows Celeste and Portal 2 dimmed |
| `/` | ↑↓ move the selection and scroll it into view; Enter opens the game |
| `/game/367520` | Banner, two stat cards, remaining cheapest-first, hidden achievement explained |
| `/game/435150` | "Rarity data unavailable" notice |
| `/game/1` | "That game is not in your library" |
| `/sync` | Progress card, four history rows |
| `/sync?scenario=invalid-key` | Two notices, "No syncs recorded yet.", disabled buttons |
| `/settings` | Accent picker changes the accent across the whole shell |
| `/onboarding` | Three cards, third dimmed |
| `/?scenario=empty` | "Nothing left to rank" |

- [ ] **Step 4: Update `CLAUDE.md`**

The "Current state" section says the UI does not exist. Replace that section with:

```markdown
## Current state

`SteamAchievements.Core` is complete and covered by tests: VDF parsing, reading
the logged-in account, the Steam API client with its error taxonomy, SQLite
storage, the sync planner and orchestrator, the ranking formula, and the
presentation layer behind the screens.

`SteamAchievements.UI` holds all six screens from the design mockup. They are
developed and verified on macOS through `SteamAchievements.Preview`, a
development-only Blazor Server host that renders the same components against
fixtures — `dotnet run --project SteamAchievements.Preview`, then
http://localhost:5100. Error and empty states are reachable there through
`?scenario=empty|invalid-key|private-profile|rarity-unknown|other-account`.

Not built yet: the WPF host itself. `SteamAchievements.Windows` is still an
empty window with no BlazorWebView reference, `ISteamPathProvider` has no
implementation, `ISecretStore` does not exist, and nothing behind the sync,
settings and onboarding screens acts. Section 14 of
`docs/superpowers/specs/2026-07-26-ui-screens-design.md` lists all of it.
```

Add to the "Commands" section:

```markdown
dotnet run --project SteamAchievements.Preview   # see the UI on macOS
dotnet build SteamAchievements.UI                # type check the components
```

Add to "Facts learned the hard way":

```markdown
- **The UI must not read through `GameRepository`.** It wraps the sync
  engine's single connection, which is not thread-safe and is already
  serialized behind a lock. `SqliteLibraryQuery` takes its own handle from
  `Database.OpenRead`; WAL lets it read while a sync writes. Note that
  `Mode=ReadOnly` is deliberately *not* used — a read-only SQLite connection to
  a WAL database still needs write access to the shared-memory index file.
- **`Virtualize` and `scrollIntoView` do not mix.** An off-screen row is not in
  the DOM, so keyboard navigation scrolls by arithmetic instead:
  `scrollTop = index * rowHeight`. That is why the queue row height is a fixed
  constant duplicated between `QueuePage.razor` and its stylesheet — a
  mismatch shows up as drift, not as an error.
```

- [ ] **Step 5: Record where the plan diverged from reality**

Append a section to `docs/superpowers/specs/2026-07-26-ui-screens-design.md`:

```markdown
## 15. Divergences from this spec during implementation

[One bullet per place where the shipped code differs from what section 1-14
described, and why. If nothing diverged, say so explicitly — an empty section
is a claim, and a claim is what the next reader needs.]
```

Fill it in from what actually happened while executing this plan. These divergences are already known before execution starts and must appear in that list:

- `QueueFilter` and `QueueCriteria` were added to `Core/Presentation`; section 5 did not list them. Filtering and sorting are real behaviour with real edge cases and belong under unit tests rather than inside a component.
- `IClock` and `ISyncPresenter` were added to `SteamAchievements.UI/State`; section 8 described only `QueueState`. Core presentation refuses to read the clock, so something has to, and the sync screen needed a seam to render against before `SyncOrchestrator` is wired up.
- `Formatting` gained minute and hour buckets that section 5.4 did not mention, because the sidebar's "Last sync 14 min ago" needs them.
- `CoverImage` was added to `Shared`; section 7 did not list it. Section 10 requires a fallback for missing cover art, and both the queue and the game screen need the same one.
- Section 7 listed `EffortMeter`, `AccountCard`, `ApiKeyField` and `DangerZone` as separate components. Each is used exactly once and carries no logic, so they were folded into their parent screens instead; `DangerZone` became a `Notice` with `Severity="Danger"`.
- The queue row component is `QueueRowCard`, not `QueueRow`: that name is already taken by the presentation record it renders.

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/ci.yml CLAUDE.md docs/superpowers/specs/2026-07-26-ui-screens-design.md
git commit -m "docs: record the shipped UI and build it on the ubuntu CI job"
```

---

## Done

Six screens, a tested presentation layer, and a way to see all of it from macOS. What is still missing to make the application run is section 14 of the spec — the WPF host, the Windows implementations, and the live data behind sync, settings and onboarding.
