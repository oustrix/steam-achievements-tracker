# UI screens — Design

Date: 2026-07-26
Source design: Claude Design project `70a57881-f220-456d-bfa1-337e7ab231f7`,
file `Steam Achievements Tracker.dc.html`.
Product design doc: `docs/specs/2026-07-26-steam-achievements-tracker-design.md`.

## 1. Goal

Turn the design mockup into real Blazor components in `SteamAchievements.UI`,
backed by a presentation layer in `SteamAchievements.Core` that is unit-tested
on macOS, and viewable locally through a development-only host.

The mockup covers six screens: completion queue, game detail, sync, settings,
onboarding, and a catalogue of empty and error states. All six are built here.

## 2. Scope

The mockup depends on three independent subsystems: the screens themselves,
the WPF + BlazorWebView host, and the Windows-specific implementations
(registry, DPAPI, clipboard). **This spec covers only the first.** Section 14
records everything the other two still need, so nothing is silently dropped.

The driving constraint is `CLAUDE.md`: the WPF project does not compile on
macOS and the application is never run locally. Everything in this spec —
Core presentation, Razor components, the preview host — builds, runs, and is
tested on macOS. Nothing here needs a Windows machine to verify.

Decisions taken during design:

- Keyboard navigation in the queue (up/down, Enter) is a real feature.
- The minimum-playtime filter is a real feature, not the dead button of the
  mockup.
- The accent colour switcher is a real, persisted setting.
- The mockup's window-width toggle (1080/1560) is mockup scaffolding; the real
  window is fluid.
- The sidebar's "Game" entry is not a navigation destination; the game screen
  is reached by drilling into a queue row.
- "Empty & error states" is not a screen. Those blocks are a shared `Notice`
  component rendered inside the screen where the condition arises.

## 3. Solution structure changes

```
SteamAchievements.Core/Presentation/   new — view models and their builders
SteamAchievements.UI/                  Razor components (replaces the template stub)
SteamAchievements.Preview/             new — net10.0 web host, development only
```

`SteamAchievements.UI.csproj` drops `<SupportedPlatform Include="browser" />`.
The application runs inside BlazorWebView, not WebAssembly, and that marker
turns the platform-compatibility analyzer loose on Core's SQLite dependency
for no benefit.

`Component1.razor`, `Component1.razor.css`, `ExampleJsInterop.cs`,
`wwwroot/background.png` and `wwwroot/exampleJsInterop.js` are template
boilerplate and are deleted.

`SteamAchievements.Preview` is added to `SteamAchievements.sln`. The Windows
publish step in `.github/workflows/ci.yml` already names
`SteamAchievements.Windows` explicitly, so the single-file check is unaffected.

## 4. Data access seam

### 4.1 `ILibraryQuery`

Declared in `Core/Presentation`. It is the only thing the components know
about data:

```csharp
public interface ILibraryQuery
{
    QueueView       GetQueue(DateTimeOffset now);
    GameDetailView? GetGame(uint appId, DateTimeOffset now);
    LibrarySummary  GetSummary(DateTimeOffset now);
    IReadOnlyList<SyncRunView> GetSyncHistory(int limit);
}
```

`now` is a parameter rather than a call to `DateTimeOffset.UtcNow`, matching
`EffortCalculator`: relative dates ("3 days ago") are presentation, and
presentation must stay testable without a clock.

Two implementations: `SqliteLibraryQuery` in `Core/Data`, and a fixture-backed
one in the preview host.

`GetQueue` returns every owned game that has achievements. Filtering, sorting
and search happen in the UI over that in-memory list — 1482 rows of small
records is nothing, and round-tripping to SQLite on every keystroke would be
worse in every way.

### 4.2 A separate read-only connection

`GameRepository` is not thread-safe: it wraps one `SqliteConnection`, several
of its methods open their own transaction, and `SyncOrchestrator` already
serializes every call to it behind a lock. If the UI read through the same
connection while a sync was running, it would reintroduce exactly that race.

`Database.Open` already sets `PRAGMA journal_mode = WAL`, which permits
readers concurrent with a writer. So the UI gets its own connection:

```csharp
public static SqliteConnection OpenRead(string path)  // no Migrate call
```

`SqliteLibraryQuery` owns that connection. No shared lock, no "only read
between syncs".

### 4.3 Schema changes

Two additions, both in `Database.Migrate`:

```sql
CREATE TABLE IF NOT EXISTS sync_runs (
    started_at   TEXT PRIMARY KEY,
    kind         TEXT    NOT NULL,   -- 'full' | 'incremental' | 'schema'
    games_synced INTEGER NOT NULL,
    duration_ms  INTEGER NOT NULL,
    error        TEXT
);
```

and an `accent TEXT` column on `settings`.

`Migrate` currently only issues `CREATE TABLE IF NOT EXISTS`, so adding a
column to an existing table needs something new. A small idempotent helper —
read `PRAGMA table_info(settings)`, issue `ALTER TABLE ... ADD COLUMN` only if
the column is absent — covers it in about ten lines and establishes the
pattern the project will need again.

Nothing writes to `sync_runs` in this spec; `SyncOrchestrator` gaining that
write belongs to the sync spec (section 14). The Sync screen therefore renders
an honest empty history rather than invented rows.

### 4.4 The one write the UI does make

The accent setting has to be stored, and section 4.2 just gave the UI a
read-only connection. That is deliberate — reads are the hot path and must
never contend — but it leaves the accent picker with nowhere to write.

So the accent goes through its own narrow service, not through `ILibraryQuery`:

```csharp
public interface IUserPreferences
{
    string? Accent { get; }
    void SetAccent(string accent);
}
```

`SqliteUserPreferences` owns a third, writable connection. WAL permits readers
alongside a writer but still allows only one writer at a time, so a click on
the accent picker during a sync would otherwise fail with `SQLITE_BUSY`. That
connection therefore sets `PRAGMA busy_timeout = 5000`; a single-row update
against `settings` finishes in microseconds, and waiting out a sync's
transaction is invisible.

Keeping this separate from `ILibraryQuery` matters: the query interface stays
honestly read-only, and the one thing the UI writes is visible in the type
system rather than hidden behind a general-purpose repository.

## 5. Presentation layer in Core

```
Core/Presentation/
  ILibraryQuery.cs
  QueueView.cs         QueueView, QueueRow, LibrarySummary
  QueueRowBuilder.cs
  ReasonWriter.cs
  GameDetailView.cs    GameDetailView, AchievementRow
  GameDetailBuilder.cs
  SyncRunView.cs
  Formatting.cs
```

Every builder is a pure static function of its inputs. No I/O, no clock, no
mutable state — the same rule `Core/Analytics` follows.

### 5.1 `QueueRow`

```csharp
public sealed record QueueRow(
    uint   AppId,
    string Name,
    int    Unlocked,
    int    Total,
    int    CompletionPercent,
    double Effort,
    string EffortText,        // "4.2", or "0" when complete
    string EffortLabel,       // "an evening"
    string Reason,
    int    PlaytimeHours,
    bool   Complete,
    bool   RarityUnknown);
```

Built from `OwnedGame`, the game's `IReadOnlyList<AchievementProgress>`, and
`EffortCalculator.Evaluate`.

**Effort label**, lifted from the mockup: effort below 8 is *an evening*,
below 25 *a few sessions*, below 80 *a long haul*, otherwise *a project*. A
complete game reads *complete*.

**Effort bar** is not part of the record. Its width depends on the largest
effort in the *currently visible* list, which changes with every filter
keystroke and is therefore unknown to a builder that sees one game at a time.
It is a separate pure function,
`QueueRowBuilder.EffortBarPercent(effort, maxEffort)`, applied by `QueuePage`
in a second pass once the list has been filtered:

`max(4, round(100 * ln(1 + effort) / ln(1 + maxEffort)))`

Linear scaling would flatten everything below Europa Universalis IV's 342
units into an invisible sliver. The floor of 4% keeps a nearly-finished game
from rendering as an empty track.

### 5.2 `ReasonWriter` — the "why it is here" line

This is the product's voice, and the one place where the mockup shows results
without stating a rule. Three of its fourteen sample lines cannot be generated
at all: *"cheapest three under one hour each"* needs per-achievement time
estimates that do not exist, and *"a second full playthrough"* and *"several
tied to Ultra-Nightmare"* were written by a human who read the achievement
list. Those are replaced by the nearest generatable form.

One threshold governs everything: **rare means below 5% of owners.** The
mockup uses 8%, 5%, 2% and 1% in different lines because it is hand-written
prose; a generator needs a single rule.

The rules, first match wins, where `n` is the number of locked achievements
and `k` counts the subset being described:

| Condition | Output |
|---|---|
| Nothing locked | `Complete — last unlock 24 Mar 2026` |
| Nothing locked, no unlock dates | `Complete` |
| Rarity unknown for all locked | `39 left, rarity unknown for all of them` |
| Rarity unknown for some (k > 0) | `39 left, rarity unknown for 6 of them` |
| Exactly one rare, and n ≤ 4 | `3 left: two common, one rare (2.1%)` |
| One or more rare | `16 left, four below 5% of owners` |
| None rare | `6 left, all above 8% of owners` |

In the last form the number is `floor` of the lowest percentage among the
locked achievements — 8 for a minimum of 8.4%, as in the mockup.

Two different count styles, as in the mockup, and they must not be confused.
The leading count is always digits — `3 left`, `16 left`, `297 left` — because
it is the line's headline number. Counts *inside* the clause are words up to
nine and digits from ten: `two common`, `four below 5%`, `41 below 1%`.
Singular is handled explicitly, so a single locked achievement reads
`1 left: one rare (2.1%)`.

This wording describes percentages, not achievability, which keeps it on the
right side of design doc §8.1: the app reports that an achievement is held by
2.1% of owners and never that it is therefore hard, dead, or not worth
attempting.

### 5.3 `GameDetailView`

```csharp
public sealed record GameDetailView(
    uint   AppId,
    string Name,
    string PlaytimeText,      // "84 h"
    string LastPlayedText,    // "3 days ago"
    int    Unlocked,
    int    Total,
    int    CompletionPercent,
    string EffortText,
    string EffortLabel,
    int    Remaining,
    string RarestText,        // "2.1%" or "unknown"
    IReadOnlyList<AchievementRow> RemainingAchievements,  // cheapest first
    IReadOnlyList<AchievementRow> UnlockedAchievements);  // most recent first
```

```csharp
public sealed record AchievementRow(
    string  Name,
    string  Description,
    string  IconUrl,
    bool    Hidden,
    double? GlobalPercent,
    string  PercentText,      // "9.8% of owners" / "rarity unknown"
    int     RarityBarPercent,
    string  CostText,         // "1.4"
    string? UnlockedDateText);
```

`RarityBarPercent` is the achievement's percentage over the game's most common
achievement — the same `maxPercent` normalisation `EffortCalculator` uses, so
the bar and the cost figure agree with each other instead of telling two
different stories.

Hidden achievements render their display name dimmed. When Steam returns an
empty description for them — which it usually does — the row shows *"Steam
returns no description for hidden achievements"* rather than a blank line.

### 5.4 `Formatting`

Playtime from minutes: `84 h`, and `48 min` below an hour. Relative dates
against the injected `now`: *yesterday*, *3 days ago*, *2 weeks ago*,
*a month ago*, *a year ago*. Absolute dates as `24 Mar 2026`. Thousands
separated by a thin space (`1 482`), as in the mockup. Number words one
through nine.

All English, per the repository language rule.

## 6. Design tokens

One `wwwroot/app.css` holds the palette as custom properties on `:root`;
everything else lives in per-component isolated CSS. The mockup styles
everything inline because it is a single-file prototype — carrying that into
Razor would throw away the one mechanism that keeps the styles from drifting
apart.

| Group | Values |
|---|---|
| Surfaces | page `#0e0d10`, shell `#141317`, sidebar `#121115`, card `#18171c`, raised `#1b1a20`, selected `#1f1d26` / `#211f28` |
| Borders | hairline `#201e26`, subtle `#232028`, default `#2b2833`, strong `#3a3444`, hover `#544c60` |
| Text | primary `#edeae3`, secondary `#c6bfb4`, muted `#9c968c`, dim `#6f6a63`, faint `#57515e` |
| Accent | `#e0a355`, hover `#f0bd7c`, dim `#7a6a52`, on-accent `#1a1509` |
| Warning | surface `#211a12`, border `#4a3b26`, text `#f0bd7c`, button `#2c2317` |
| Danger | surface `#1f1517`, border `#4a2626`, text `#e89898`, button `#2a191b` / `#6b3232` |
| Meters | track `#282531`, complete `#4c4650`, inactive dot `#3d3844` |
| Scrollbar | thumb `#34303a` on a transparent track |

The accent is `--accent` on the shell root, so the switcher rewrites one
variable instead of twenty rules. Its four values come from the mockup:
`#e0a355` amber, `#8fb3c9` blue, `#c98f7a` terracotta, `#a8b58c` olive.

Type: Instrument Sans for prose, JetBrains Mono for every number, identifier,
timestamp and uppercase micro-label. That split is load-bearing in the
mockup — it is what makes counts read as data rather than as copy.

## 7. Components

```
Layout/      AppShell, NavItem, SyncStatusCard
Queue/       QueuePage, QueueToolbar, QueueRow, EffortMeter, ProgressBar
Game/        GamePage, GameHeader, StatCard, RemainingRow, UnlockedRow
Sync/        SyncPage, SyncProgressCard, SyncHistoryRow
Settings/    SettingsPage, AccountCard, ApiKeyField, CacheRow, AccentPicker, DangerZone
Onboarding/  OnboardingPage, StepIndicator
Shared/      EmptyState, Notice
```

`Notice` takes a severity (info, warning, danger), a title, body text and an
optional action. All five blocks from the mockup's states screen are `Notice`
with different parameters:

| State | Placed in |
|---|---|
| Steam rejected the API key (401) | Sync, with its action opening Settings |
| Game details are private | Sync |
| A different Steam account is signed in | Sync |
| Rarity data unavailable | Game screen, when `RarityUnknown` |
| Paused after five consecutive failures | Sync, from the circuit breaker |

`EmptyState` covers "Nothing left to rank" and an empty search result.

`QueueToolbar` holds title search, the minimum-playtime filter (Any / 1 h /
5 h / 20 h), the "100 % complete: hidden/shown" toggle, three sort buttons
(Effort, Completion, Playtime) and the count line. Effort sorts ascending by
default — least work first — while Completion and Playtime default to
descending; clicking the active button flips direction.

`SettingsPage` shows the two cache TTLs read from `SyncOptions.Default`
(30 days for schema, 7 for global percentages) as values, not editors. They
are decisions the sync engine owns; presenting them as fields would imply a
control that does not exist.

## 8. Navigation and state

Real Blazor routing, not a screen enum: `/`, `/game/{AppId:int}`, `/sync`,
`/settings`, `/onboarding`. `NavigationManager` behaves the same in
BlazorWebView and in the preview host, so nothing gets rewritten when the WPF
host arrives, and the preview host becomes directly addressable — `/sync`
opens the sync screen without clicking through to it.

`QueueState` is a scoped service holding sort key, direction, search text,
minimum playtime, hide-complete, and the selected app id. It raises a change
event that `QueuePage` subscribes to. Its whole reason to exist is that the
mockup is one application with one state: select a row, press Enter, come
back, and the selection and sort are still there. Component-local state would
lose them on every drill-down.

## 9. Virtualization and keyboard navigation

The queue holds around 1500 rows; design doc §9 calls for virtualization, and
a plain list would put roughly twenty thousand nodes in the DOM.
`<Virtualize>` with a fixed row height handles it — rows are a fixed 96px
cover plus padding.

The fixed height also solves the keyboard problem. `scrollIntoView` on the
selected row does not work under virtualization, because an off-screen row is
not in the DOM to scroll to. With a constant row height the scroll position is
arithmetic: `scrollTop = index * rowHeight`, set through a three-line JS
interop helper. Up and down move the selection, Enter navigates to the game
screen, and hovering a row selects it, as in the mockup.

## 10. Assets

**Fonts are vendored.** Instrument Sans and JetBrains Mono go into
`wwwroot/fonts` as woff2, wired up with `@font-face`. A desktop application
should not wait on `fonts.googleapis.com` at every start, or lose its
typography offline. Both are OFL; the licence files ship alongside.

**Cover art comes from Steam's CDN** by direct URL —
`library_600x900.jpg` in the queue, `header.jpg` on the game screen. Not every
app id has a `library_600x900.jpg`, so every image needs a fallback: on error
the tile becomes the diagonal-hatch placeholder from the mockup with the game
name over it. On-disk caching of cover art is out of scope (section 14).

## 11. Preview host

`SteamAchievements.Preview` renders the real components from the RCL. It has
no markup of its own — a second copy of the screens would immediately become a
second source of truth.

What it does have is fixtures and a scenario selector in the query string:
`?scenario=empty`, `invalid-key`, `private-profile`, `rarity-unknown`,
`other-account`. This is not a gallery of state cards; it is a way to feed
`ILibraryQuery` the data under which each state actually occurs. Without it an
error state cannot be seen without genuinely breaking an API key.

The fixture set is the fourteen games from the mockup — Hollow Knight through
Portal 2 — with their real app ids, plus Hollow Knight's achievement list for
the game screen. Real app ids mean real cover art from the CDN, so the layout
is checked against real proportions instead of grey rectangles.

The project is development-only: it is in the solution and built in CI, but
never published.

## 12. Testing

All tests go in `SteamAchievements.Core.Tests` and run under
`dotnet test SteamAchievements.Core.Tests` on macOS.

- `ReasonWriter` — all seven forms, both sides of the 5% threshold, the n ≤ 4
  boundary, singular and plural, and the word/digit switch at ten.
- `Formatting` — playtime below and above an hour, each relative-date bucket,
  thousands separation.
- `QueueRowBuilder` — the four effort labels at their boundaries, the
  logarithmic bar including its 4% floor, and a complete game.
- `GameDetailBuilder` — remaining sorted cheapest first, unlocked sorted most
  recent first, hidden achievements, rarity-bar normalisation.
- `SqliteLibraryQuery` — against a temporary database, following the existing
  `GameRepositoryTests` pattern, including a game with no rarity data at all.
- `SqliteUserPreferences` — accent round-trips, and reading a database whose
  `settings` row does not exist yet returns null rather than throwing.
- `Database.Migrate` — the `accent` column is added to a database created
  before it existed, and running the migration twice is a no-op.

No bUnit project. With the logic in Core the components are renderers, and a
test project for them does not pay for itself yet. Revisit when a component
grows behaviour beyond a click.

Visual verification is the preview host.

## 13. CI

The ubuntu job gains `dotnet build SteamAchievements.UI` and
`dotnet build SteamAchievements.Preview`. Today a broken Razor component is
caught only by the Windows job, three minutes later; on ubuntu it is caught in
seconds.

## 14. Out of scope — still required for a working application

None of this is done here. It is listed so the gap is explicit.

**The WPF host.** The `Microsoft.AspNetCore.Components.WebView.Wpf` package is
not referenced at all; `MainWindow.xaml` is an empty `<Grid/>`. Needed: the
`BlazorWebView` control, the root component and host page, service
registration in `App.xaml.cs`, and window chrome.

**Windows implementations.** `ISteamPathProvider` is declared but has no
implementation — `RegistrySteamPathProvider` over
`HKCU\Software\Valve\Steam\SteamPath`. `ISecretStore` does not exist even as
an interface; it and `DpapiSecretStore` are both needed. Plus the clipboard
watcher that recognises 32 hex characters, and opening the browser on Steam's
key issuance page.

**Live data behind the screens.** The sync screen renders completely but
Pause and Cancel do nothing, and progress, request rate and ETA are not wired
to `SyncOrchestrator`. Writing to `sync_runs` from `SyncOrchestrator` belongs
here too. In settings, Replace key, Change account and Reset database render
but do not act. Onboarding is three finished screens with no logic behind
them.

**Persona name and avatar.** The mockup shows both; they come from the public
`?xml=1` profile endpoint, which `SteamApiClient` does not implement.

**Last sync time.** The sidebar shows "Last sync 14 min ago" from
`settings.last_full_sync_at`, a column that exists but is never written.

**Cover art caching on disk.** Download once, invalidate, clean up — a
subsystem of its own.

**bUnit component tests**, per section 12.

Already recorded as out of scope for the MVP in design doc §10 and unchanged
here: DLC separation, trend charts, the full library grid, friend comparison,
and code signing.
