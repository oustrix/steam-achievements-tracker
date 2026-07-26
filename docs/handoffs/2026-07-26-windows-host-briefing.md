# Windows host — briefing for a parallel workstream

Written 2026-07-26 from the `worktree-design-ui` branch, which builds the six
UI screens. This document exists so that work can be specced **now**, in
parallel, without re-deriving what the UI branch already settled.

**How to use it.** Brainstorm and write the spec from this document today.
Do not start implementing until the UI branch lands — the host's entire job is
to host those components, and half of what is described below does not exist
on `main` yet. Branch from `worktree-design-ui`, not from `main`, when the
time comes.

**Do not edit** `docs/superpowers/plans/2026-07-26-ui-screens.md` or
`docs/superpowers/specs/2026-07-26-ui-screens-design.md`. Both are being
amended as the UI branch executes.

---

## 1. What this workstream is

Section 14 of the UI spec lists everything deliberately left out of that
plan. This is that list, and it is the whole remaining distance between "six
screens that render" and "an application someone can use".

**In scope:**

- The WPF host: `BlazorWebView`, `MainWindow`, DI registration, window chrome.
- `RegistrySteamPathProvider` — the unimplemented half of an interface that
  already exists.
- `ISecretStore` (does not exist even as an interface) and `DpapiSecretStore`.
- Clipboard watching for the API key, and opening the browser on Steam's key
  page.
- The live data behind the sync, settings and onboarding screens: real
  progress, pause, cancel, replace key, change account, reset database.
- Writing `sync_runs` and `settings.last_full_sync_at`, which are read today
  but never written.
- The public `?xml=1` profile endpoint for persona name and avatar.

**Out of scope** — already decided and recorded in design doc §10: DLC
separation, trend charts, the full library grid, friend comparison, code
signing.

---

## 2. The defining constraint

`SteamAchievements.Windows` does not compile on macOS, and the application is
never run there. Every line that lands in that project is verified only by
push → GitHub Actions → download artifact → run on a Windows machine, roughly
three to five minutes per cycle.

**This is the single strongest force on the design.** The UI branch responded
to it by pushing every decision it could into `SteamAchievements.Core`, behind
interfaces, and by building `SteamAchievements.Preview` so the screens could
be seen locally. This workstream should apply the same pressure: anything that
can be a pure function in Core, or an interface with a fake implementation,
should be. What remains in the Windows project should be thin enough to read
and believe without running it.

Concretely, a registry read and a DPAPI call are each about five lines. The
logic around them — deciding which account is active, validating that a
clipboard string is a key, choosing what to do when the key is rejected —
belongs in Core where `dotnet test SteamAchievements.Core.Tests` covers it.

---

## 3. What already exists to plug into

Signatures below are current on `worktree-design-ui`. Anything marked NEW was
added by the UI branch and is not on `main`.

### Data and storage

```csharp
// SteamAchievements.Core.Data
static SqliteConnection Database.Open(string path);       // migrates
static SqliteConnection Database.OpenRead(string path);   // NEW — no migrate
static void Database.Migrate(SqliteConnection connection);// NEW — public, idempotent

sealed class GameRepository(SqliteConnection connection); // the sync engine's writer
```

`settings` is a single row with `id = 1` and columns `steam_id64`,
`persona_name`, `avatar_url`, `last_full_sync_at`, `accent` (NEW). Nothing
writes the first four yet. `sync_runs` (NEW) is
`(started_at TEXT PK, kind TEXT, games_synced INTEGER, duration_ms INTEGER, error TEXT)`
where `kind` is `full`, `incremental` or `schema`; it is read but never
written.

### The seams the UI consumes

```csharp
// SteamAchievements.Core.Presentation — all NEW
interface ILibraryQuery {
    QueueView       GetQueue(DateTimeOffset now);
    GameDetailView? GetGame(uint appId, DateTimeOffset now);
    LibrarySummary  GetSummary(DateTimeOffset now);
    IReadOnlyList<SyncRunView> GetSyncHistory(int limit, DateTimeOffset now);
}
interface IUserPreferences { string? Accent { get; } void SetAccent(string accent); }

// SteamAchievements.Core.Data — all NEW
sealed class SqliteLibraryQuery(SqliteConnection connection) : ILibraryQuery;
sealed class SqliteUserPreferences(SqliteConnection connection) : IUserPreferences;

// SteamAchievements.UI.State — NEW
interface IClock { DateTimeOffset Now { get; } }
sealed class SystemClock : IClock;
```

`ISyncPresenter` and `SyncStatusView` land in the UI branch's Task 13 and are
what the sync screen renders. Its production implementation on that branch is
`IdleSyncPresenter`, which reports "no sync running" — **replacing that with a
real one driven by `SyncOrchestrator` is this workstream's job.**

### Sync and Steam

```csharp
// SteamAchievements.Core.Sync
sealed class SyncOrchestrator(SteamApiClient, GameRepository, SyncOptions, TimeSpan? retryBaseDelay);
Task SyncOrchestrator.RunAsync(ulong steamId, bool force, IProgress<SyncProgress>?, CancellationToken);
sealed record SyncProgress(int Completed, int Total, string CurrentGame);
sealed record SyncOptions(TimeSpan SchemaTtl, TimeSpan GlobalTtl);  // Default: 30d / 7d

// SteamAchievements.Core.Steam
sealed class SteamApiClient(HttpClient http, string apiKey);
enum SteamApiErrorKind { InvalidKey, NoStatsForApp, RateLimited, ServerError, BadRequest, Unknown }
// SteamApiException carries Kind and IsTransient.

// SteamAchievements.Core.Local
static IReadOnlyList<SteamAccount> LoginUsersReader.Read(string vdfText);
static SteamAccount? LoginUsersReader.SelectActive(IReadOnlyList<SteamAccount>);
sealed record SteamAccount(ulong SteamId64, string AccountName, string PersonaName,
                           bool MostRecent, DateTimeOffset Timestamp);

// SteamAchievements.Core.Abstractions
interface ISteamPathProvider { string? FindSteamPath(); }   // declared, NOT implemented
```

`SteamApiClient` has no profile lookup. The persona name and avatar the
onboarding and settings screens show come from the public `?xml=1` endpoint,
which has to be added.

### The UI

Routes are `/`, `/game/{AppId:int}`, `/sync`, `/settings`, `/onboarding`, all
declared inside the Razor Class Library with `AppShell` as the layout.

**`SteamAchievements.Preview/Program.cs` is your working reference for DI
registration.** It is a Blazor Server host rather than a WebView one, but the
service graph the components expect is identical, and it is known to work.
Read it before writing the WPF equivalent.

---

## 4. Decisions already made — do not re-litigate

- **Three SQLite connections, not one.** `GameRepository` is not thread-safe:
  it wraps a single connection, several of its methods open their own
  transaction, and `SyncOrchestrator` already serializes every call to it
  behind a lock. The UI therefore reads through its own connection from
  `Database.OpenRead`, and preferences write through a third. WAL permits
  readers concurrent with a writer. The host owns all three lifetimes.
- **`Mode=ReadOnly` is deliberately not used.** A read-only SQLite connection
  to a WAL database still needs write access to the shared-memory index file,
  so that mode fails in exactly the configuration the reader exists for. The
  read-only guarantee is by construction — callers issue only SELECTs.
- **The preferences connection needs `PRAGMA busy_timeout`.** WAL allows one
  writer at a time, so a click during a sync would otherwise fail with
  `SQLITE_BUSY`. `Database.OpenRead` sets 5000ms; whatever connection you hand
  `SqliteUserPreferences` must too.
- **The sidebar has three entries** — queue, sync, settings. The game screen is
  a drill-down, onboarding is a first-run gate rather than a destination, and
  error states render where their condition arises.
- **Rarity is reported as a number, never as a verdict** (design doc §8.1).
  Nothing in the host should add "this looks unobtainable" language.
- **Fonts are vendored**, not loaded from a CDN. Cover art is loaded from
  Steam's CDN by URL with a placeholder fallback.

---

## 5. Hazards, learned the hard way

- **`PublishSingleFile` and WebView2.** CI asserts that `publish/` contains
  exactly one file, and that check already caught native libraries shipping
  loose once — hence `IncludeNativeLibrariesForSelfExtract`. Adding
  `Microsoft.AspNetCore.Components.WebView.Wpf` brings WebView2 native
  binaries with it. **Expect that check to fail on the first push and budget
  for it.** Decide deliberately whether the answer is to pack them, or to
  relax the check with a recorded reason.
- **WebView2 runtime presence.** `BlazorWebView` needs the Evergreen WebView2
  runtime on the target machine. It ships with Windows 11 and current Windows
  10, but not with every install. Decide what the app does when it is absent —
  failing with a blank window is the default and it is a bad one. This is not
  mentioned anywhere in the existing design doc.
- **Dapper mapping.** Columns must be aliased explicitly (`SELECT app_id AS AppId`)
  — Dapper does not translate snake_case. Every INTEGER column comes back as
  `Int64`, and Dapper's record materializer needs an exact CLR type match, so
  row records declare `long` and narrow in the projection. A `uint` or `int`
  there throws at read time, and only on multi-row queries — a one-row test
  will not catch it.
- **`SyncOrchestrator` has no pause.** It has cancellation. The sync is
  resumable because progress is written per game, so "pause" can plausibly be
  cancel-and-resume — but that is a design decision to make explicitly, not to
  discover while wiring a button. See the open questions below.
- **Never run a bare `dotnet build` or `dotnet test` at the repository root.**
  It fails with NETSDK1100 because the solution contains a `net10.0-windows`
  project. That failure is about the host platform, not about your change.
  Always name the project.
- **Anonymise before committing.** Real SteamID64 values and API keys must
  never land in the repository, including inside fixtures and design assets.
  This has already been caught once on the UI branch.

---

## 6. Genuinely open — for the brainstorm to settle

These are not oversights; nothing in the project has decided them yet.

1. **Where does the database live?** No path is chosen anywhere.
   `%LOCALAPPDATA%` is the obvious candidate, but the choice interacts with
   backup, with the "reset database" action, and with what happens when two
   Windows users share a machine.
2. **What does Pause mean?** `SyncOrchestrator` offers cancellation, not
   suspension. Cancel-and-resume is cheap and honest given per-game
   resumability; a real pause primitive in Core is more work and more code in
   the place that is hardest to verify. Pick one and say why.
3. **First-run sequencing.** The shell reads `ILibraryQuery.GetSummary` on
   every render, and onboarding is a route rather than a wizard that owns the
   window. What does the app show between launch and a completed onboarding —
   does the router redirect, does the shell render in a degraded state, and
   what does the sidebar's sync card say when there is no database yet?
4. **Clipboard watching is privacy-sensitive.** The app polls the user's
   clipboard looking for 32 hex characters. Decide the polling interval, when
   watching starts and stops, and — more importantly — what the UI tells the
   user about it. The onboarding screen currently says "watching clipboard for
   32 hex characters", which is honest, but the behaviour should not outlive
   the screen that discloses it.
5. **Key rejection at runtime.** `SteamApiException` with
   `Kind == InvalidKey` can surface mid-sync, long after onboarding. Where
   does the user land, and does the stored key get cleared or merely flagged?
6. **Changing accounts.** Cached data belongs to one SteamID64. The mockup has
   a state for "a different Steam account is signed in" and the settings
   screen has a "Change account" button. Blending two libraries would make the
   ranking meaningless, so switching implies discarding — decide whether that
   is automatic, confirmed, or refused.

---

## 7. Sequencing

The UI branch is executing sixteen tasks; when this was written it had
finished eight. Its final task updates `CLAUDE.md` and the CI workflow, so
expect a conflict there if this workstream touches either — coordinate rather
than merging blind.

Specification work has no dependency on the UI branch at all. Implementation
has a total one.
