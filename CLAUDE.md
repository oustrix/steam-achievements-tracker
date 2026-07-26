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
dotnet run --project SteamAchievements.Preview   # see the UI on macOS
dotnet build SteamAchievements.UI                # type check the components
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
