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
SteamAchievements.Core        net10.0          all logic; builds/tests on macOS
SteamAchievements.Core.Tests  net10.0          runs on macOS and Linux CI
SteamAchievements.UI          net10.0          Razor Class Library (Blazor)
SteamAchievements.Windows     net10.0-windows  WPF host + Windows API impls
```

**Boundary rule:** code that parses text or talks HTTP belongs in Core and is
tested on macOS. Code that calls Windows APIs (registry, DPAPI, clipboard)
belongs in `SteamAchievements.Windows`, behind an interface declared in
`Core/Abstractions`. Never let `Microsoft.Win32` or `System.Security.Cryptography.ProtectedData`
leak into Core — it breaks local development entirely.

## Commands

```bash
dotnet test SteamAchievements.Core.Tests   # the local feedback loop
dotnet build SteamAchievements.Core        # quick type check
dotnet format                               # before committing
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
- Tests never hit the network. They replay recorded fixtures from `testdata/`
  through a substituted `HttpMessageHandler`.
- Fixtures must be anonymized before committing: strip `key=`, replace real
  `steamid` values. A committed API key is a leaked credential.

## Current state

`SteamAchievements.Core` holds all the logic and is covered by unit tests: VDF
parsing, the logged-in account, the Steam API client and its error taxonomy, the
public profile endpoint, SQLite storage, the sync planner and orchestrator, the
sync state machine, onboarding, account administration, and the ranking formula.

`SteamAchievements.UI` holds the Blazor components. `SteamAchievements.Preview`
is a development-only host that renders them from macOS against fixtures.
`SteamAchievements.Windows` is the real host: a WPF window with a
`BlazorWebView`, plus the four Windows-only classes — registry, DPAPI, shell,
WebView2 probe.

Data lives in `%LOCALAPPDATA%\SteamAchievementsTracker\`: `library.db` and
`apikey.bin`.

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
  the executable. CI has a step that fails the build if `publish/` contains more
  than one file.
- **Steam's error responses are HTML, not JSON**, and a missing key returns 400
  or 401 depending on the endpoint. See `docs/steam-api.md`.
- **The publish check needs `-Recurse`.** `Get-ChildItem publish -File` does not
  see subdirectories, and both WebView2's loader and BlazorWebView's static
  assets publish into them. Without `-Recurse` the check passes green on an
  artifact that is not a single file.
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
