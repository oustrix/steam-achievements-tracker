# Steam Achievements Tracker

Windows desktop app that pulls a Steam library and ranks games by how
realistic it is to complete their achievements next.

Design: `docs/specs/2026-07-26-steam-achievements-tracker-design.md`
Steam API facts: `docs/steam-api.md`

## Repository language

Everything committed is in **English** — code, comments, documentation, commit
messages, UI strings, issue text. This is an open-source project and must stay
readable to everyone.

## Development environment — read this before proposing verification

- Development happens on **macOS**. The application is **never run locally**.
- `SteamAchievements.Windows` (WPF) **does not compile on macOS**. Do not
  suggest building or running it here; it is built only in CI.
- The only local verification is `dotnet test` over `SteamAchievements.Core`.
  Treat "it compiles and tests pass on macOS" as the bar for a change to be
  ready to push.
- Everything else is verified by: push → GitHub Actions → download artifact →
  run on a separate Windows machine (~3-5 min per cycle). Optimize for keeping
  logic out of the Windows project so this loop is rarely needed.

## Layout and the boundary rule

```
src/SteamAchievements.Core          net10.0          all logic; builds/tests on macOS
src/SteamAchievements.UI            net10.0          Razor Class Library (Blazor)
src/SteamAchievements.Windows       net10.0-windows  WPF host + Windows API impls
src/SteamAchievements.Cli           net10.0          headless sync, runs on macOS
src/SteamAchievements.Preview       net10.0          dev-only Blazor Server host

tests/SteamAchievements.Core.Tests  net10.0          runs on macOS and Linux CI
tests/testdata                                       recorded HTTP fixtures
```

**Boundary rule:** code that parses text or talks HTTP belongs in Core and is
tested on macOS. Code that calls Windows APIs (registry, DPAPI, clipboard)
belongs in `SteamAchievements.Windows`, behind an interface declared in
`Core/Abstractions`. Never let `Microsoft.Win32` or `System.Security.Cryptography.ProtectedData`
leak into Core — it breaks local development entirely.

## Commands

```bash
dotnet test tests/SteamAchievements.Core.Tests   # the local feedback loop
dotnet build src/SteamAchievements.Core          # quick type check
dotnet format src/SteamAchievements.UI           # before committing
dotnet run --project src/SteamAchievements.Preview   # see the UI on macOS
dotnet build src/SteamAchievements.UI                # type check the components
```

Always name the test project. A bare `dotnet test` at the repository root
fails on macOS with NETSDK1100, because the solution includes the
`net10.0-windows` WPF project. That failure is about the host platform, not
about your change.

## Working with the Steam API

- **Do not guess endpoint behaviour from memory.** `docs/steam-api.md` records
  what was verified with live requests, including inconsistent status codes
  and HTML error bodies. Consult it; if reality disagrees, re-verify with the
  commands at the bottom of that file and update it.
- Tests never hit the network. They replay recorded fixtures from `tests/testdata/`
  through a substituted `HttpMessageHandler`.
- Fixtures must be anonymized before committing: strip `key=`, replace real
  `steamid` values. A committed API key is a leaked credential.

## Current state

`SteamAchievements.Core` holds all the logic and is covered by unit tests: VDF
parsing, the logged-in account, the Steam API client and its error taxonomy, the
public profile endpoint, SQLite storage, the sync planner and orchestrator, the
sync state machine, onboarding, account administration, and the ranking formula.

`SteamAchievements.UI` holds all six screens from the design mockup. They are
developed and verified on macOS through `SteamAchievements.Preview`, a
development-only Blazor Server host that renders the same components against
fixtures — `dotnet run --project src/SteamAchievements.Preview`, then
http://localhost:5100. Error and empty states are reachable there through
`?scenario=normal|empty|invalid-key|private-profile|rarity-unknown|other-account|circuit-open|first-run`.
`first-run` is the only way to see `AppShell`'s onboarding guard on macOS.

`SteamAchievements.Windows` is the real host: a WPF window with a
`BlazorWebView`, plus the four Windows-only classes — registry, DPAPI, shell,
WebView2 probe. Data lives in `%LOCALAPPDATA%\SteamAchievementsTracker\`:
`library.db`, `apikey.bin` and `log.txt`.

Every screen is wired to its service: `ISyncController`, `IOnboarding` and
`IAccountAdmin` are reachable from the sync, onboarding and settings screens.

Logging goes through `ILogger<T>`. The file sink lives in `Core/Diagnostics`
and writes `log.txt` beside `library.db`, rotating at 2 MB across four files.
Nothing is filtered: the application has never run on Windows, and the first
failure has to be in the file already. Redaction is structural, not a
call-site convention — see the fact below on `TextLoggerProvider`.
`docs/windows-first-run.md` is the checklist for that first run.

## Facts learned the hard way

These cost real debugging time. Do not rediscover them.

- **Dapper cannot map into `ValueTuple`**, and does not translate `snake_case`
  columns to PascalCase. Every query aliases its columns explicitly
  (`SELECT app_id AS AppId`) and materializes into a private row record.
- **SQLite returns `Int64` for every INTEGER column**, and Dapper's constructor
  materializer needs an exact CLR type match. Row records use `long` and narrow
  in the projection; declaring `uint` throws at runtime on multi-row queries.
- **`GameRepository` is not thread-safe.** One `SqliteConnection` cannot serve
  overlapping transactions, so `SyncOrchestrator` serializes every repository
  call behind a lock while keeping HTTP concurrent. A two-game test fixture
  hides this completely; a real library breaks constantly.
- **`PublishSingleFile` is not enough for WPF.** Without
  `IncludeNativeLibrariesForSelfExtract`, native libraries ship loose next to
  the executable. CI has a step that fails the build if any turn up there.
- **Steam's error responses are HTML, not JSON**, and a missing key returns 400
  or 401 depending on the endpoint. See `docs/steam-api.md`.
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
- **The published artifact is an exe plus a `wwwroot` tree, not one file.**
  BlazorWebView's static assets travel through the static web assets pipeline
  rather than as `<Content>`, so `IncludeAllContentForSelfExtract` does not
  reach them — measured, not assumed. Every assembly and native library *is*
  inside the 67 MB exe. The CI check therefore asserts "no loose `.dll`/`.pdb`
  anywhere, and no other `.exe`", which is the failure it was written to catch;
  it uses `-Recurse`, without which it cannot see subdirectories at all and
  passes green on anything.
- **`Microsoft.Web.WebView2` copies its loader twice.** Its `Common.targets`
  adds `WebView2Loader.dll` as a `Content` item linked into
  `runtimes\win-x64\native\`, separately from the RID asset `PublishSingleFile`
  bundles. `WebView2NeverCopyLoaderDllToOutputDirectory` turns the extra copy
  off.
- **The isolated-CSS bundle is named after the host assembly.** The WPF host
  links `SteamAchievements.Windows.styles.css`; Preview links
  `SteamAchievements.Preview.styles.css`. Getting it wrong gives a fully working
  but completely unstyled application.
- **DPAPI `CurrentUser` blobs are unreadable from another Windows profile.**
  `DpapiSecretStore.Read` catches `CryptographicException` and returns null,
  which is the same state as "no key stored". That is deliberate, not a
  swallowed error.
- **A `<Router>` with no `<NotFound>` renders nothing.** Not an error, not a
  message — a blank window. The WPF host's `Routes.razor` carries one.
- **The WPF host uses `Microsoft.NET.Sdk.Razor`, not `Microsoft.NET.Sdk`.** It
  compiles `.razor` files, which the plain SDK ignores, and `UseWPF` still
  applies. It also includes `wwwroot/**` as content by default, so adding an
  explicit `<Content Include="wwwroot\**\*" />` fails the build with NETSDK1022
  instead of being harmlessly redundant.
- **The sync seam lives in `Core/Presentation`, not in `UI/State`.** It was
  written in both places by two parallel branches. It has to be in Core: a seam
  declared in the UI project cannot be implemented there, which would strand the
  whole state machine in the WPF project. `SyncPhase` says what is happening,
  `SyncProblem` says what is blocking, and they are deliberately separate — a
  rejected key leaves the sync idle *and* blocked.
- **A `DelegatingHandler` cannot tell a caller's cancellation from an
  `HttpClient` timeout.** `HttpClient` never passes the caller's token down the
  handler pipeline; it links it into an internal token it also cancels when its
  own `Timeout` elapses, so a `catch (OperationCanceledException) when
  (token.IsCancellationRequested)` in a handler is true either way and can
  never evaluate false at that position — established by probe, not from
  memory. The same guard one layer up, in `SteamApiClient.GetJsonAsync`, does
  discriminate, because there the token is still the caller's own, untouched by
  `HttpClient`. `LoggingHandler` logs both cases at Debug for exactly this
  reason.
- **`builder.Logging.SetMinimumLevel()` is a no-op in a host with an
  `appsettings.json`.** It only sets `LoggerFilterOptions.MinLevel`, the
  fallback used when no `LoggerFilterRule` matches — and any
  `Logging:LogLevel:Default` entry in configuration always contributes a rule,
  which always wins. Measured, not assumed: `SteamAchievements.Preview`'s
  Debug floor lives in `appsettings.json` and `appsettings.Development.json`,
  not in the `SetMinimumLevel` call that looks like it sets it.
- **`Redaction.Scrub` must be reachable from exactly one shared path that every
  logger provider goes through, not called at each provider's write site.**
  When it had a single caller, the invariant "nothing unredacted reaches a
  sink" held only for that one sink — and the moment the CLI got a second one,
  a stock `AddSimpleConsole`, it printed the real Steam API key to stdout on
  every request, because that provider knows nothing about `Redaction`. The
  fix is structural: `TextLoggerProvider` is an abstract base whose private
  `Write` composes `LogLine.Format` and `Redaction.Scrub` once, in that order;
  `RollingFileLoggerProvider` and `ConsoleLogProvider` are both thin
  subclasses that supply only a destination and inherit the invariant rather
  than having to remember it.

## Reviewing your own plans

The plan in `docs/superpowers/plans/` was written without the ability to run
it, and reviews found real defects in eight of its nine tasks — mutable shared
state, unhandled malformed input, a concurrency hazard, a silent no-op that
would have disabled the sync cache entirely. Its final section records each
divergence.

Treat plan code as a proposal, not as truth: **the shipped code and `git log`
are authoritative.** When implementing from a plan and a test fails against the
plan's own implementation, report the discrepancy instead of adjusting the test
to match.

## Conventions

- The ranking formula lives in `Core/Analytics` and is pure and unit-tested —
  no I/O, no clock. It is the product's core value and must stay easy to
  reason about.
- Sync progress and cancellation flow through `CancellationToken` and
  `IProgress<T>`; nothing in Core blocks or sleeps without a token.
- Prefer small, focused files. When one grows past a few hundred lines, that
  usually means a boundary is missing.
