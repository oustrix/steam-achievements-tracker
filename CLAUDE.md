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
dotnet test                                  # the local feedback loop
dotnet build SteamAchievements.Core          # quick type check
dotnet format                                # before committing
```

## Working with the Steam API

- **Do not guess endpoint behaviour from memory.** `docs/steam-api.md` records
  what was verified with live requests, including inconsistent status codes
  and HTML error bodies. Consult it; if reality disagrees, re-verify with the
  commands at the bottom of that file and update it.
- Tests never hit the network. They replay recorded fixtures from `testdata/`
  through a substituted `HttpMessageHandler`.
- Fixtures must be anonymized before committing: strip `key=`, replace real
  `steamid` values. A committed API key is a leaked credential.

## Conventions

- The ranking formula lives in `Core/Analytics` and is pure and unit-tested —
  no I/O, no clock. It is the product's core value and must stay easy to
  reason about.
- Sync progress and cancellation flow through `CancellationToken` and
  `IProgress<T>`; nothing in Core blocks or sleeps without a token.
- Prefer small, focused files. When one grows past a few hundred lines, that
  usually means a boundary is missing.
