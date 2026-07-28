# Windows Host — Design

Branch: `worktree-windows-host`, based on `worktree-design-ui` (not on `main`).
The host exists to run the components that branch builds, so it must sit on
top of them.

Prerequisites: `docs/specs/2026-07-26-steam-achievements-tracker-design.md`
(the product design, especially §5 structure, §9 screens, §10 non-goals, §11
distribution) and `docs/steam-api.md`.

## 1. What this covers

Everything needed to turn a library of Blazor components into a running
Windows application:

- the WPF host — `BlazorWebView`, `MainWindow`, composition root, window chrome
- `RegistrySteamPathProvider` over the already-declared `ISteamPathProvider`
- `ISecretStore` (new) and its DPAPI implementation
- opening a browser at Steam's API key page
- live data behind the screens: real sync progress, pause, cancel; replacing
  the key, switching accounts, resetting the database; onboarding logic
- writing `sync_runs` and `settings.last_full_sync_at`, which are read today
  and written by nobody
- the public `?xml=1` profile endpoint for persona name and avatar

Out of scope, settled elsewhere: everything in design doc §10 (DLC splitting,
trend charts, the full library grid, friend comparison, code signing), plus
the additions recorded in §11 of this document.

## 2. The constraint that shapes everything

`SteamAchievements.Windows` does not compile on macOS and the application is
never run locally. Every line in that project is verified by push → GitHub
Actions → artifact → a separate Windows machine, three to five minutes per
cycle.

The UI branch answered this by moving everything it could into Core behind
interfaces and building a development host so the screens could be seen
locally. This design does the same, and the target is explicit: the WPF
project should end up around 150 lines, matching the estimate in design doc
§5, with no logic in any of them.

The test of whether a piece belongs in Core is not "does it feel like UI
code" but "can a `dotnet test` on macOS tell me it is wrong". Reading a
registry value cannot. Deciding what to do when Steam rejects a key can, and
therefore must.

## 3. Decisions taken during design

Six questions were genuinely open. All six are settled here; the reasoning is
recorded because the alternatives will look attractive again later.

### 3.1 Where the database lives

`%LOCALAPPDATA%\SteamAchievementsTracker\` — `library.db` alongside
`apikey.bin`.

The decisive argument is consistency with DPAPI. A `CurrentUser`-scoped blob
is already per-Windows-user by construction. A shared database next to a
per-user key would give a second Windows user someone else's library that
they cannot sync. Putting both in `%LOCALAPPDATA%` makes "two Windows users
on one machine" resolve itself: two users, two databases, two keys, no shared
state and no code.

Rejected: `%APPDATA%` (Roaming), because a roaming profile would drag a
hundred-megabyte database across the network and WAL over SMB breaks;
next-to-the-executable portable mode, because `Program Files` is not
writable and two Windows users running one copy from `Downloads` would share
a database while holding different keys; a `--data-dir` override, because a
second code path costs a Windows verification cycle to check and buys a
scenario nobody has asked for. It stays easy to add later.

Path resolution is a pure function in Core. Only the call that asks Windows
for the folder itself stays in the host.

### 3.2 What "Pause" means

Pause is cancellation with a remembered state. There is no suspension inside
`SyncOrchestrator`.

A 1500-game library is roughly 2700 requests at a ceiling of five per second
— about nine minutes. Wanting to walk away from that is legitimate, so the
affordance is real; the mechanism behind it does not have to be.

Resumption is genuine rather than simulated: `MarkSynced` is written per
game, so `SyncPlanner` skips what is already done. Resuming costs one extra
`GetOwnedGames` call to rebuild the plan.

Rejected: a gate that four workers await inside `Parallel.ForEachAsync`,
next to cancellation and a circuit breaker. It buys the preservation of four
in-flight requests, and pays for it with a new class of deadlock risk in the
one component that is both currently correct and impossible to verify
locally.

Consequence, accepted deliberately: pausing produces physically separate
runs, so one logical sync with two pauses leaves three rows in `sync_runs`.
Every run gets a row, and a cancelled one carries its reason in `error`. The
history screen already reads that column, and "the sync was interrupted" is
exactly the fact people open a history to find.

What a `sync_runs` row contains, so this is not decided twice:

| Column | Value |
|---|---|
| `started_at` | when `Start` was called, UTC |
| `kind` | `full` when `Start(force: true)`, otherwise `incremental` |
| `games_synced` | the last `SyncProgress.Completed` observed |
| `duration_ms` | elapsed until the run ended, however it ended |
| `error` | `null` on success, the rejection message on failure, `cancelled` when paused or cancelled |

The schema permits a third `kind`, `schema`, which nothing in this design
produces. It is left alone rather than removed — the column is text and a
future schema-only refresh is the obvious use for it.

### 3.3 What is shown before onboarding completes

Two distinct windows of time, with different answers.

**Process start to first paint.** WebView2 takes hundreds of milliseconds to
initialize, during which the window is visible and empty. `MainWindow.xaml`
carries a static XAML placeholder beneath the `BlazorWebView`, removed on
`BlazorWebViewInitialized`. This is also where the "WebView2 is missing"
message lands (§3.7), which is the larger reason it exists.

**First paint to completed onboarding.** The load-bearing fact is that
`BlazorWebView` has no address bar and no back button: the user can only go
where a link we drew takes them. The gate does not need to intercept
anything, only to avoid rendering chrome.

Three cheap pieces rather than one clever one:

1. the host sets `BlazorWebView.StartPath = "/onboarding"` when the state is
   incomplete;
2. the onboarding page uses its own chromeless layout;
3. `AppShell` navigates to `/onboarding` when the state is incomplete, which
   costs two lines and covers "the database was reset while running".

"Onboarding is complete" is a pure function of two inputs — a stored
SteamID64 and the presence of a key — and lives in Core.

**Connection ordering is not free.** `Database.OpenRead` deliberately does not
migrate. If the reader connection is opened before the writer, `GetSummary`
fails on a missing `settings` table on a clean machine. The composition root
opens the writer first and carries a comment saying why, because otherwise
this gets reordered the first time somebody tidies the DI registrations.

### 3.4 Clipboard watching

There is none.

The original plan was to watch the clipboard for a 32-character key during
onboarding. It is dropped, and dropping it removes more than one feature: with
no clipboard watching there is no P/Invoke, no window message hook and no
`Clipboard.GetText()` anywhere in the WPF project. The key field is a plain
Blazor `<input>`; Ctrl+V already works. The `IClipboardWatcher` interface the
design would have needed does not get written.

The host keeps one obligation from this area: opening a browser at Steam's key
issuance page.

### 3.5 A key revoked mid-sync

**The key is flagged, not erased.** `SteamApiClient.Classify` maps 401, 403
*and 400* onto `InvalidKey`, and 400 is Steam's workhorse status — it answers
"no stats for this app", a malformed parameter and an interstitial page with
the same code. A false `InvalidKey` is therefore a matter of time, not a
hypothesis. Erasing the DPAPI blob on a false positive sends the user back to
Steam's website for a key that was fine; the opposite error costs one banner.
The key is erased only on an explicit replace or a full reset.

The flag is `settings.key_rejected_at TEXT`, added through the existing
idempotent `EnsureColumn` path. It is persisted rather than held in memory:
otherwise a restart makes the application look healthy, and the user spends
requests rediscovering what was already known. It is cleared when a new key
is accepted or when a sync succeeds.

**The user is not moved anywhere.** Error states are drawn where the condition
arises:

- the sync screen shows the rejection with a link to settings, and the
  progress figures stay on screen so it is clear where it stopped;
- the sidebar's sync card reflects the same state, because it *is* the sync
  indicator and a rejected key is a sync state;
- onboarding is not re-entered — the user already has a SteamID and half a
  library, and the "is this you?" step has nothing to offer them;
- already-downloaded data is kept, because it is correct.

This is why key state is part of what the host hands to the screens rather
than something the screens work out for themselves (§5.1).

### 3.6 Switching accounts

Neither `games` nor `owned_games` nor `player_achievements` carries a SteamID
column. The database implicitly belongs to one account, so mixing two
libraries does not produce something ugly — it produces silently wrong data
with no marker in the schema to tell them apart afterwards.

Switching is offered, and it wipes, and it asks first: "This will delete the
cached library for <persona>. 1 482 games, 8 214 achievements."

Rejected: forbidding it outright, which makes the user hunt for "switch
account" and find "delete everything"; doing it automatically when
`loginusers.vdf`'s active account changes, because people switch to a second
or regional account and back, and silently destroying their data for it is
indefensible; supporting several accounts side by side, which design doc
§10.3 already excludes.

**A mismatch is noticed, not acted upon.** When the stored SteamID differs
from Steam's currently active account, the stored one remains authoritative
and a line appears in settings offering the confirmed switch. Not a startup
modal: this is an observation rather than an error, and hijacking a launch
with it is hostile.

**Reset cannot delete the file.** Three connections are open against it and
Windows does not delete open files. Reset is therefore `DELETE FROM` across
every table in one transaction, a commit, and then `VACUUM` separately, since
it cannot run inside a transaction. That is plain SQLite work, so it belongs
in Core under `dotnet test` rather than in the project where checking it
costs five minutes.

### 3.7 Packaging and the WebView2 runtime

Two traps, both examined by reading the packages rather than from memory.

**The single-file check has a hole.** `Microsoft.AspNetCore.Components.WebView.Wpf`
10.0.90 (versioned with MAUI, not with ASP.NET) depends on
`Microsoft.Web.WebView2` 1.0.3179.45. That package's `Common.targets`, for
managed projects, adds `WebView2Loader.dll` as a `Content` item with
`<Link>runtimes\win-x64\native\WebView2Loader.dll</Link>` — a loose native
library in a *subdirectory*, separate from the same file arriving as a proper
RID asset that `IncludeNativeLibrariesForSelfExtract` does bundle. On top of
that, `BlazorWebView` reads static assets from disk: the host's
`wwwroot/index.html` plus `_content/SteamAchievements.UI/` with `app.css`,
`queue-scroll.js` and ten woff2 fonts.

The CI check is `Get-ChildItem publish -File` — with no `-Recurse`. It cannot
see subdirectories. An artifact containing a `wwwroot/` tree and a
`runtimes/win-x64/native/` tree passes it green and is declared a single file.
The trap does not fire as a failed build; it fires as a false pass, and is
discovered a full Windows cycle later.

So the check is tightened rather than relaxed:

- `-Recurse` on the check. This is mandatory under every outcome below — the
  check currently does not verify what it claims to.
- `IncludeAllContentForSelfExtract=true` in the host project, so content is
  bundled and extracted to `%TEMP%\.net\` on first launch of a given build.
- `WebView2NeverCopyLoaderDllToOutputDirectory=true`, the package's own
  documented property, visible in the condition on the `ItemGroup` above.

Rejected: shipping a folder and rewriting the check as a filename allowlist,
which is cheaper but discards the "download one exe and run it" property that
design doc §11 rests on; embedding static assets as resources behind a custom
file provider, which is the most code and the most risk, in the least
verifiable project, for what two properties should deliver.

**Outcome, verified 2026-07-26 — the hypothesis was wrong, and the fallback
was taken.** `IncludeAllContentForSelfExtract` does not reach BlazorWebView's
static assets, because they are not `Content` items: they travel through the
static web assets pipeline, which is why the publish output carries a
`staticwebassets.endpoints.json`, fingerprinted names like
`SteamAchievements.UI.nq9585yljm.bundle.scp.css`, and `.br`/`.gz` variants of
every text file. No MSBuild property bridges those two mechanisms.

What did work is the part that mattered: the executable is 67 MB with every
assembly and native library inside it, and **`WebView2Loader.dll` does not
appear anywhere in the output**, so
`WebView2NeverCopyLoaderDllToOutputDirectory` closed the trap this section was
written about. The tree beside the exe is entirely css, js, fonts and their
compressed variants.

So the artifact is one executable plus a `wwwroot` tree, not one file. The
self-contained property that design doc §11 actually rests on — no .NET Desktop
Runtime to install — is untouched; what is lost is the cosmetic "one file to
download", and downloads arrive as a zip regardless.

The CI check was rewritten rather than relaxed. Demanding one file asserts
something unachievable, so it now asserts what the check was written to catch
in the first place: **no `.dll` or `.pdb` anywhere in the output, and no
executable other than the host's**. A loose native library beside the exe still
fails the build, which is the failure mode that motivated the check; a
stylesheet no longer does.

**A missing WebView2 runtime** otherwise produces an empty window. The host
calls `CoreWebView2Environment.GetAvailableBrowserVersionString()` before
constructing the `BlazorWebView`; on `WebView2RuntimeNotFoundException` it
never constructs it, and the placeholder from §3.3 carries an explanation and
a button to the Evergreen Bootstrapper page. Around fifteen lines, reusing
markup that exists for another reason. The README says the same thing, because
someone who sees that message is more likely to go read the README than to
press the button.

Rejected: bundling the bootstrapper and installing it, which needs elevation
and turns the application into an installer; Fixed Version distribution, which
adds roughly 180 MB on top of the current 80–100.

## 4. What moves into Core

### 4.1 `Core/Abstractions`

```csharp
public interface ISecretStore
{
    string? Read();
    void Write(string secret);
    void Clear();
}
```

No name parameter. There is exactly one secret, and a general-purpose store is
an invitation to put something else in it.

### 4.2 `Core/App`

| Type | Responsibility |
|---|---|
| `DataPaths.Resolve(baseDirectory)` | Paths to `library.db` and `apikey.bin` under `SteamAchievementsTracker\`. The host supplies `baseDirectory` from `Environment.GetFolderPath`. |
| `ApiKey.TryNormalize` | 32 hex characters after `Trim()`, normalized to upper case. Rejects the mess a paste produces: quotes, whitespace, 31 or 33 characters, non-hex. |
| `OnboardingState.Evaluate(storedSteamId, hasKey)` | `ChooseAccount` / `EnterKey` / `Ready`. Two inputs, one output — the whole of §3.3's "is onboarding complete". |
| `SteamAccountLocator(ISteamPathProvider)` | Reads `<steam>/config/loginusers.vdf` and returns accounts. Reading a file is not a Windows API; a fake path provider pointing at `testdata/` tests it, including "no path" and "no file". |

### 4.3 `Core/Data`

- `Database.ResetLibrary(connection)` — `DELETE FROM` across every table in a
  transaction, commit, then `VACUUM`. Clears games, achievements, history,
  `steam_id64`, `persona_name`, `avatar_url`, `last_full_sync_at`,
  `key_rejected_at`. **Keeps `accent`**: that is the user's taste rather than
  the account's data, and losing it on "switch account" is a surprise beyond
  what was promised.
- `IAccountStore` / `SqliteAccountStore` — reads and writes the identity
  columns of `settings` and the key-rejected flag. Kept apart from
  `IUserPreferences` so that interface keeps its honest framing as the only
  thing the UI writes.
- `SyncJournal` — writes a `sync_runs` row and `settings.last_full_sync_at`,
  after `RunAsync` returns, when no sync transaction is in flight.
- Migration: one more `EnsureColumn(connection, "settings", "key_rejected_at",
  "TEXT")`. The mechanism exists and is already proven on `accent`.

**`SyncRunView` has to change.** It currently carries `bool Failed`, and
`SqliteLibraryQuery.Describe` renders any non-null `error` as
`"Failed — {error}"`. Under §3.2 a cancelled run stores `cancelled` in that
column, so the history screen would report every pause as a failure — the
opposite of what §3.2 argues the column is for. `bool Failed` becomes

```csharp
public enum SyncRunOutcome { Completed, Cancelled, Failed }
```

and `Describe` grows a cancelled branch rendering `"Cancelled — 412 games"`.
Nothing consumes `Failed` yet, so this is free today and only gets more
expensive.

There is one reset operation, not two. "Switch account" and "reset the
database" are identical as far as the schema is concerned; the difference
lives in the host's composition, where it is visible:

- **Switch account** = `ResetLibrary` + write the new identity. The key is
  untouched — a Steam API key is not bound to an account and can query any
  public profile.
- **Reset everything** = `ResetLibrary` + `ISecretStore.Clear()`.

### 4.4 `Core/Steam`

```csharp
public sealed class SteamCommunityClient(HttpClient http)
{
    Task<PublicProfile?> GetProfileAsync(ulong steamId64, CancellationToken ct);
}
```

A separate class rather than a method on `SteamApiClient`: a different host
(`steamcommunity.com`), a different format (XML, not JSON), different error
semantics and no key. It does not belong next to `GetJsonAsync<T>`.

**This call never blocks onboarding.** No answer, an HTML body, a redirect to
`/login` — the "is this you?" step shows the SteamID without an avatar and
lets the user continue. The name and the picture are decoration, not data.

Verified live on 2026-07-26 rather than recalled, because the shape decides
the parser:

- A profile answers `<profile>` with `<steamID64>`, `<steamID>` (the persona
  name), `<avatarFull>` and more. Every text field is wrapped in `CDATA`.
- A profile whose `privacyState` is `friendsonly` still returns the name and
  the avatar. Privacy does not have to be handled as a failure.
- **A non-existent profile answers HTTP 200** with
  `<response><error>The specified profile could not be found.</error></response>`.
  The status code is therefore useless; the parser must branch on the root
  element being `profile`.
- `/games?tab=all&xml=1` still redirects anonymous callers to `/login` with a
  302, so `docs/steam-api.md` remains accurate on that point.

These facts belong in `docs/steam-api.md`, which is the authoritative record;
the plan adds them there.

The test fixture is recorded live and anonymized before committing, per the
repository rule on `steamid` and `key`.

## 5. Contracts with the screens

### 5.1 Sync state

Declared in `Core/Presentation`, alongside `ILibraryQuery` and
`IUserPreferences`.

This is a deliberate departure from the original briefing, which placed
`ISyncPresenter` in `UI.State`. The reason is not symmetry: an interface in
`SteamAchievements.UI` cannot be implemented in Core, so the entire sync state
machine would land either in the WPF project or in an adapter layer. Nothing
is lost by the move — `IdleSyncPresenter` was never written.

```csharp
public enum SyncPhase { NeverRun, Idle, Running, Paused, Failed, KeyRejected }

public sealed record SyncStatusView(
    SyncPhase Phase, int Completed, int Total,
    string CurrentGame, string Headline, string? Detail, string? Error);

public interface ISyncPresenter { SyncStatusView Status { get; } event Action? Changed; }
public interface ISyncController { void Start(bool force); void Pause(); void Cancel(); }
```

Six phases: exactly the distinctions the screens need, including the one §3.5
requires. There is no `Resume` method — resuming is `Start(force: false)`, and
the screen picks the button's label from `Phase`. The difference between pause
and cancel then lives in one enum instead of being spread across four methods.

`SyncCoordinator` in Core implements both. It owns the
`CancellationTokenSource`, writes `sync_runs` through `SyncJournal`, and sets
and clears `key_rejected_at`.

### 5.2 The seam that makes it testable

`SyncCoordinator` depends not on `SyncOrchestrator` but on

```csharp
public interface ISyncRunner
{
    Task RunAsync(ulong steamId, bool force, IProgress<SyncProgress>? progress, CancellationToken ct);
}
```

with a five-line adapter over the real orchestrator. The state machine is then
tested against a fake that reports scripted progress and throws scripted
exceptions: paused mid-run, `InvalidKey` on the four-hundredth game, cancelled,
network failure, success. Milliseconds per test instead of standing up the
whole fixture apparatus.

Without this seam, the only way to answer "what happens when a key is revoked
mid-sync" is a Windows cycle. That is the single highest-value piece of design
in this document.

`SyncCoordinator` takes time as a `Func<DateTimeOffset>` rather than through
`IClock`: `IClock` lives in `UI.State`, and pulling it into Core would mean
touching a neighbouring branch's file for the sake of one delegate.

### 5.3 The threading contract

An `IProgress<SyncProgress>` created on a background thread invokes its
callback on the thread pool. `Changed` therefore does **not** arrive on the
render thread. Components must wrap their reaction in
`InvokeAsync(StateHasChanged)`.

This is not style. `ILibraryQuery` sits on a single `SqliteConnection`, so
calling `GetSummary` from a background thread while a render is in progress is
concurrent use of that connection — corruption, not an exception. `InvokeAsync`
is the only thing serializing every read. It is a mandatory item for the UI
branch.

The same applies to `IOnboarding` and `IAccountAdmin` (§5.4): both expose an
`event Action? Changed` in the shape `QueueState` already established, and both
are subject to the same rule. `ISyncPresenter` is the only one whose event
actually fires off-thread today, but a component written to the rule stays
correct if that changes.

No progress throttling: 2700 reports over nine minutes is about five per
second.

### 5.4 The remaining services

Interfaces in `Core/Presentation`, implementations there too except the last:

- `IOnboarding` — the current step, discovered accounts, choosing an account,
  submitting a key, and manual SteamID64 entry for when Steam is absent or the
  user is not signed into it.

  Submitting a key is format check → trial request to Steam → store, and
  nothing is stored unless Steam accepts it. The result has four values, not
  two: a malformed key spends no request at all, and a Steam that cannot be
  reached is kept separate from a Steam that refused — the advice for the first
  is "try again", for the second "get another key". One `GetOwnedGames` call
  answers this in under a second; without it the user learns their key is bad
  several minutes into their first sync.
- `IAccountAdmin` — the stored account, the detected active Steam account, the
  confirmed switch, the full reset.
- `IExternalLinks` — open Steam's key page, open the data folder. Implemented
  in the WPF project.

Manual entry accepts a 17-digit number or a `/profiles/<id>` URL. Vanity URLs
(`/id/<name>`) are **not** accepted: that is another endpoint absent from
`docs/steam-api.md`, and adding it drags in live verification. Separate task
if it is ever wanted.

## 6. What stays in `SteamAchievements.Windows`

Four classes, none of which contains logic:

| Class | Size | Notes |
|---|---|---|
| `RegistrySteamPathProvider` | ~8 lines | `HKCU\Software\Valve\Steam\SteamPath`; normalize forward slashes |
| `DpapiSecretStore` | ~30 lines | `ProtectedData`, scope `CurrentUser`. `CryptographicException` on read returns `null` — a blob from another Windows profile is unreadable by construction, and that is the same state as "no key was ever stored" |
| `ShellLinks` | ~10 lines | `Process.Start` with `UseShellExecute = true` |
| `WebView2Probe` | ~10 lines | `GetAvailableBrowserVersionString` |

Plus the composition root and the window.

## 7. Composition

Lifetimes: a `BlazorWebView` hosts exactly one window and one session, so
everything that `Preview/Program.cs` registers as `Scoped` is registered here
as **`Singleton`** — including `QueueState`, whose selection and sort must
survive a drill-down into a game and back.

`Preview/Program.cs` is the working reference for the service graph. The host
type differs; the set of services does not.

Three connections, in a fixed order:

```
1. Database.Open(paths.DatabaseFile)      writer, migrates
2. Database.OpenRead(paths.DatabaseFile)  UI reader, does not migrate
3. Database.Open(paths.DatabaseFile)      settings and journal, busy_timeout
```

`OpenRead` creates no tables. Open it first and `GetSummary` fails on a
missing `settings` table on a clean machine. This carries a comment in the
composition root, because otherwise it is reordered the first time somebody
tidies the registrations.

Why three and not one, restated because it is easy to "simplify" away:
`GameRepository` is not thread-safe and `SyncOrchestrator` already serializes
every call to it behind a lock, so sharing that connection with the UI would
put reads back inside the same contention. WAL permits a reader alongside a
writer. `Mode=ReadOnly` is deliberately not used: a read-only connection to a
WAL database still needs to write the shared-memory index and fails in exactly
the configuration the reader exists for. The settings connection needs
`busy_timeout` because WAL permits only one writer, and a click on the accent
picker during a sync would otherwise fail with `SQLITE_BUSY`.

### 7.1 The window

`MainWindow.xaml` is a `Grid` with two layers: the placeholder underneath
(dark background, application name), the `BlazorWebView` on top, initially
`Collapsed`. The WebView is shown and the placeholder hidden on
`BlazorWebViewInitialized`. If `WebView2Probe` reported no runtime, the
WebView is never constructed and the placeholder changes its text (§3.7).

The `Window` background is dark so a resize does not flash white. Default size
1100×720, minimum 900×600. Remembering window placement is out of scope.

`Routes.razor` and `wwwroot/index.html` are written fresh for this project and
are **not** shared with `Preview`: that host is server-rendered with
`@rendermode`, this one needs a static `index.html` for the WebView. The
duplication is real and unavoidable, recorded here so nobody tries to merge
them.

One detail in `index.html` is worth naming because getting it wrong produces
a fully working but completely unstyled application, which is a whole Windows
cycle to diagnose: the bundle of per-component isolated CSS is named after the
*host* assembly. `Preview/Components/App.razor` links
`SteamAchievements.Preview.styles.css`; this host must link
`SteamAchievements.Windows.styles.css`. The RCL's own `app.css` and
`queue-scroll.js` keep their `_content/SteamAchievements.UI/` paths.

## 8. Error states

The principle is settled: a state is drawn where its condition arises. This
table exists so no condition is left without a screen.

| Condition | Where | What the user can do |
|---|---|---|
| WebView2 runtime missing | XAML placeholder instead of the WebView | Button to the Evergreen Bootstrapper page |
| `Database.Open` threw at startup | The same placeholder | The data folder path is shown; otherwise this is a blank window |
| Steam not found in the registry | Onboarding, account step | Enter a SteamID64 or a profile URL manually |
| `loginusers.vdf` present, no accounts | The same | The same |
| Profile returned no name or avatar | "Is this you?" step | Continue — the step is not blocked |
| Key fails the format check | Key field | Nothing is sent to Steam |
| Steam rejected the key during onboarding | Key field | Enter another |
| Steam unreachable while checking the key | Key field | Retry — the key is not blamed and not stored |
| Steam rejected the key mid-sync | Sync screen and the sidebar card | "Replace key" → settings; progress figures stay |
| Sync failed on the network | Sync screen | Retry; the run is recorded in `sync_runs` with `error` |
| Active Steam account differs from the stored one | Settings, as a line | Go to the confirmed switch |
| Library empty, never synced | `EmptyState` on the queue | Start the first sync |
| DPAPI blob unreadable | Onboarding, key step | Paste the key again |

The last row is not hypothetical: a `CurrentUser` blob is physically
unreadable from another Windows profile or after a reinstall. It is caught and
turned into `null`, which is the same state as "there was never a key" — no
separate branch is needed.

## 9. Testing

Under `dotnet test` on macOS:

- `DataPaths`
- `ApiKey.TryNormalize`, including the mess a paste produces
- `OnboardingState.Evaluate` across every input
- `Database.ResetLibrary`: data gone, schema intact, `accent` preserved,
  idempotent
- `SqliteAccountStore`: identity round-trip, key-rejected flag
- `SyncJournal`
- `SteamAccountLocator` against `testdata/`, including "no path" and "no file"
- `SteamCommunityClient` against a recorded fixture, including an HTML body
  and a redirect
- **`SyncCoordinator` — the whole state machine** against a fake `ISyncRunner`

The last item is the point of §5.2. It turns "a key was revoked mid-sync" from
a question only a Windows machine can answer in five minutes into one
`dotnet test` answers in a second.

### 9.1 What only Windows can verify

> **Superseded.** The living checklist is `docs/windows-first-run.md`, which
> carries these seven items plus what the log must show for each. This section
> is left as the record of what the host design asked for.

Verified in a single deliberate pass rather than five fishing trips:

1. `publish/` really does contain one file — under the **recursive** check
2. the application starts and draws the queue rather than an empty window
3. RCL static assets arrived: fonts, `app.css`, per-component isolated CSS,
   `queue-scroll.js`
4. the registry is read and the account is found
5. DPAPI: write a key, restart, read it back
6. the key page opens in a browser
7. the placeholder gives way to the WebView instead of staying up

## 10. CI

- `Get-ChildItem publish -File` gains `-Recurse`. Mandatory under every
  outcome — the check currently does not verify what it claims to.
- The host project gets `IncludeAllContentForSelfExtract=true` and
  `WebView2NeverCopyLoaderDllToOutputDirectory=true`, each with a comment
  recording where it came from.
- If the artifact is still not a single file, fall back to the
  folder-plus-allowlist option from §3.7 with the reason written down.

### 10.1 Coordinating with the UI branch

This branch is based on `worktree-design-ui`. Both touch `CLAUDE.md` and
`.github/workflows/ci.yml`, so conflicts are expected rather than surprising.

What this design requires from the UI branch:

- an `/onboarding` route with a chromeless layout
- a redirect in `AppShell` when onboarding is incomplete
- consuming `ISyncPresenter` and `ISyncController` from `Core/Presentation`
- `InvokeAsync(StateHasChanged)` around every reaction to `Changed` (§5.3)
- `SyncStatusCard` taking `SyncStatusView` rather than only `LibrarySummary`.
  It currently renders two strings out of the summary; §3.5 requires it to
  show a rejected key, which the summary cannot express.
- the history screen reading `SyncRunView.Outcome` instead of the `bool Failed`
  it replaces (§4.3).

Note that as of `worktree-design-ui` at 99bc25b the UI branch has one screen,
not six: `Queue/QueuePage.razor` on `/`, plus `AppShell`, `SyncStatusCard`,
`NavItem`, `QueueToolbar` and four shared components. `/game/{AppId:int}`,
`/sync`, `/settings` and `/onboarding` do not exist yet, and neither does
`ISyncPresenter` in any form. This design therefore *defines* those contracts
rather than consuming them.

## 11. Out of scope

Beyond design doc §10: vanity URL resolution, remembering window placement, a
`--data-dir` override, several accounts side by side, true suspension inside
`SyncOrchestrator`, and installing the WebView2 runtime automatically.

## 12. Divergences from this spec during implementation

Recorded during execution rather than reconstructed afterwards.

- **`LiveSyncRunner` was added to `Core/Sync`.** §5.2 described only a
  "five-line adapter over the real orchestrator". It turned out to need
  `ISecretStore`, because the key can be replaced while the application runs and
  a `SyncOrchestrator` built once at startup would keep using the old one. It
  also became the natural home for "no key is stored", which it reports as
  `InvalidKey` so the state machine lands on the screen that can fix it.
- **`SyncCoordinator` uses a private `InlineProgress<T>` rather than
  `System.Progress<T>`.** `Progress<T>` captures a SynchronizationContext and
  posts asynchronously, which would let the recorded `games_synced` lag behind
  the run it belongs to and would make the pause test a race. The coordinator's
  tests run in 52 ms and were stable across five consecutive runs because of
  this choice.
- **`SyncCoordinator.Completion` is public.** §5.2 did not mention it. The
  composition root needs it to await an in-flight sync before disposing the
  SQLite connections; without it, shutdown during a sync leaves the
  orchestrator's worker pool writing into disposed handles.
- **`SyncJournal.MarkSyncCompleted` writes `settings.last_full_sync_at` after
  every successful run, not only after a full one.** The column name predates
  the distinction and the sidebar reads it as "last sync".
- **`IOnboarding.SubmitKeyAsync` replaced a synchronous `SubmitKey`, and returns
  four outcomes rather than a boolean.** Found by reviewing the plan against
  §8's error table: that table has a row for a key Steam rejects during
  onboarding, but the service as first drafted only checked the format, so the
  row described a state the code could not reach. See §5.4.
- **`Routes.razor` in the WPF host carries a `<NotFound>` section.** Not in the
  plan. The host sends a first-run user to `/onboarding`, which the UI project
  has not built yet, and a `Router` without `NotFound` renders nothing at all —
  the blank window this design exists to avoid. It will stay useful after that
  route lands.
- **`SyncRunView.Failed` became `SyncRunOutcome Outcome`** (§4.3), which also
  required updating `SteamAchievements.Preview`'s fixture query. Caught by
  building the preview host immediately after the change rather than at the end.
- **The assembly is still named `SteamAchievements.Windows`,** so the published
  artifact is `SteamAchievements.Windows.exe`. Renaming it would be nicer for a
  download but changes the isolated-CSS bundle name in `index.html` too; it is a
  cosmetic change and was left for its own commit.
- **§5.1's phase model was replaced by the UI branch's, which is better.**
  This spec proposed six phases with `KeyRejected` and `Failed` among them. The
  UI branch, working in parallel, had already shipped `SyncPhase` (`Idle`,
  `Running`, `Paused`, `CircuitOpen`) alongside a separate `SyncProblem`
  (`None`, `InvalidKey`, `PrivateProfile`, `OtherAccount`), and its screens were
  built against it. Splitting "what is happening" from "what is blocking" is the
  right cut: a rejected key leaves the sync idle *and* blocked, and folding both
  into one enum forces every future state to pick a side it does not belong on.

  §5.1's *location* argument survived intact and decided the merge — the types
  moved from `SteamAchievements.UI/State/` into `Core/Presentation`, because a
  seam declared in the UI project cannot be implemented in Core, which would
  have left the whole state machine in the WPF project. `IdleSyncPresenter` was
  deleted; `SyncCoordinator` implements the seam directly.

  `SyncStatusView` also carries `EtaText` and `RateText`, which the progress
  card already rendered and this spec never mentioned. `SyncProgressReport` in
  `Core/App` computes them, with tests for the degenerate cases — nothing
  completed, no elapsed time, a total of zero.

- **The screens' buttons are still inert.** `SyncPage`, `SettingsPage` and
  `OnboardingPage` render real state through the unified seam, but their
  controls remain `disabled`: binding them to `ISyncController`, `IOnboarding`
  and `IAccountAdmin` is UI work in the components, deliberately left as its own
  task rather than folded into the merge.

- **Not verified on Windows yet.** Everything in §6, §7 and §10 is written but
  unexecuted. §9.1 is the checklist for the first push, and until it has been
  run this section cannot claim the host works — only that it compiles the parts
  macOS can compile. `Database.ResetLibrary` running `VACUUM` while the reader
  and writer connections are open belongs on that checklist: it passes
  single-connection tests, but WAL behaves differently with live neighbours.
