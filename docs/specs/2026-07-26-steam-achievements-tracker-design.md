# Steam Achievements Tracker — Design

Date: 2026-07-26
Status: approved, ready to be broken down into tasks

## 1. Goal

A Windows desktop application that pulls in the user's Steam library and helps
decide which game to complete to 100% next.

Distributed as a regular program: everyone downloads it and connects their own
account. The app works with a single account — whichever one is logged into
Steam on that machine.

## 2. Development environment constraints

These drive many of the decisions below, so they come first.

- Development happens on macOS. The application is never run locally.
- Verification path: push → GitHub Actions → download artifact → run on a
  separate Windows machine. Roughly a 3-5 minute cycle.
- Consequence: anything verifiable through `dotnet test` on macOS must be
  verified that way. Only what cannot physically be checked otherwise goes
  through CI.
- WPF does not compile on macOS. Windows-specific code is isolated in its own
  project behind interfaces.

## 3. Accessing Steam data

### 3.1 Verified fact: an API key is mandatory

Verified with live requests on 2026-07-25 (details in `docs/steam-api.md`):

- Community XML (`/games?tab=all&xml=1`), which used to serve the library
  without a key, now redirects anonymous callers to `/login` (HTTP 302).
- `GetOwnedGames` without a key returns HTTP 401.
- Still public: `/profiles/<id>/?xml=1` (persona name, avatar) and
  `GetGlobalAchievementPercentagesForApp` (global rarity).

There is no legitimate way to read a library and personal achievements without
a key. The goal is therefore not to avoid the key, but to make obtaining it
nearly invisible.

### 3.2 Rejected authentication options

| Option | Why rejected |
|---|---|
| Steamworks SDK (`steam_api.dll`) | Only works from inside a game with its own appid; cannot read an arbitrary library |
| Steam client protocol login (`steamcmd`-style) | Requires password and 2FA, ToS grey area, puts the account at risk |
| Developer key hardcoded into the binary | The 100k/day quota would be shared by all users, and the key would be extracted from the binary |
| Backend proxy holding our key | Same quota problem plus hosting costs; contradicts the local-application model |

### 3.3 Chosen approach: hybrid

1. **SteamID — zero user action.** Steam path from the registry
   (`HKCU\Software\Valve\Steam\SteamPath`), then `config/loginusers.vdf`, then
   the account flagged `MostRecent="1"`. Persona name and avatar for
   confirmation come from the public `?xml=1` endpoint.
2. **API key — a single paste.** A button opens the browser on the key
   issuance page, where the user is already signed in. Meanwhile the app
   watches the clipboard and fills the key in automatically once it sees 32
   hex characters.
3. **Key storage** — DPAPI scoped to the current Windows user. The key is
   never written to disk in plaintext.

Fallback when Steam cannot be located: manual entry of a SteamID64 or profile
URL.

Known limitation: Steam only issues API keys to accounts with at least $5 in
purchases. Handled with explanatory text during onboarding rather than a
failure.

## 4. Stack

| Layer | Choice |
|---|---|
| Platform | .NET 10 (LTS, 10.0.302 locally) |
| Shell | WPF + BlazorWebView (WebView2) |
| UI | Blazor components (Razor Class Library) |
| Storage | SQLite |
| CI | GitHub Actions |

Rationale: the project's complexity splits between the sync engine (network,
concurrency, resilience) and the UI (a 1500-row grid, charts later). .NET is
strong at the former, Blazor covers the latter with web technology while
staying in one language, and Windows specifics (registry, DPAPI, installer)
are native territory for .NET.

Rejected: Fyne/Avalonia and native GUI toolkits generally — a virtualized grid
and charts become a project of their own there; Electron — bundle size and a
weaker sync engine; Rust/Tauri — the learning cost does not pay off here.

Blazor shell: WPF + BlazorWebView chosen as the most well-trodden path.
Rejected alternatives were ASP.NET Core in a browser tab (does not feel like
an application) and Photino (niche technology, few answers when it breaks).

## 5. Solution structure

```
SteamAchievements.Core            net10.0          builds and tests on macOS
├─ Steam/         SteamApiClient, rate limiter, retry, DTOs
├─ Local/         VdfParser, LoginUsersReader          (pure text parsing)
├─ Data/          SQLite, schema, migrations, repositories
├─ Sync/          SyncOrchestrator, scheduler, progress
├─ Analytics/     achievement cost, ranking
└─ Abstractions/  ISteamPathProvider, ISecretStore     contracts only

SteamAchievements.Core.Tests      net10.0          dotnet test on macOS
SteamAchievements.UI              net10.0          Razor Class Library
SteamAchievements.Windows         net10.0-windows  WPF + BlazorWebView,
                                                   RegistrySteamPathProvider,
                                                   DpapiSecretStore (~150 lines)
```

Boundary rule: code that parses text (VDF, JSON) lives in Core and is tested
on macOS. Code that calls Windows APIs (registry, DPAPI) lives in
`SteamAchievements.Windows` behind an interface declared in `Abstractions`.

## 6. Data model

```
settings             steam_id64, persona_name, avatar_url, api_key_protected,
                     last_full_sync_at
games                app_id PK, name, icon_hash, has_achievements,
                     schema_synced_at
owned_games          app_id PK, playtime_forever, playtime_2weeks,
                     last_played_at
achievements         app_id, api_name (PK), display_name, description,
                     icon_url, icon_gray_url, is_hidden,
                     sort_order, first_seen_at
global_percents      app_id, api_name (PK), percent, fetched_at
player_achievements  app_id, api_name (PK), unlocked, unlocked_at
sync_state           app_id PK, last_sync_at, last_error
snapshots            taken_at, unlocked_total, avg_rarity, completion_pct
```

At 1500 games this is roughly 60 thousand achievement rows — negligible for
SQLite. The bottleneck is entirely in the network.

Two fields are collected ahead of time and unused in the MVP:

- `sort_order` and `first_seen_at` — groundwork for separating base game from
  DLC achievements later (see 10.1). Neither can be reconstructed
  retroactively.
- `snapshots` — one row per sync, the basis for future trend charts. History
  cannot be backfilled, so it is written from day one.

`steam_id64` in `settings` is checked at startup: if the user switched Steam
accounts, the app says so instead of silently blending two libraries. That is
one check, not multi-account support.

## 7. Sync engine

Sequence:

1. `GetOwnedGames` — one request, the whole library with playtime.
2. The scheduler selects games where `playtime_forever` changed, or which were
   never synced, or whose schema (30-day TTL) or global percentages (7-day
   TTL) went stale.
3. Queue processing: `Parallel.ForEachAsync`, 4-6 workers on top of a token
   bucket limited to ~5 requests per second.
4. `sync_state` is written after every game, making a sync resumable across
   application restarts.
5. A snapshot row is written at the end.

**The key optimization.** `GetOwnedGames` returns playtime for every game in a
single cheap request. If playtime has not changed, achievements almost
certainly have not either, so the progress request is skipped entirely.

```
Full sync (1500 games, ~60% with achievements):  1 + 900×3 ≈ 2700 requests, ~9 min
Incremental sync after a gaming session:         ~5 requests, ~2 seconds
```

Resilience: Polly with exponential backoff (1→2→4→8 s) on 429 and 5xx,
honouring `Retry-After`; retries are forbidden on 401 since that means a bad
key rather than a network fault; five consecutive failures trip a circuit
breaker, pausing the sync with a message in the UI.

Response handling and error codes live in `docs/steam-api.md`.

## 8. Ranking formula

```
relative(a)  = clamp( percent(a) / max(percent across this game's achievements) )
absolute(a)  = clamp( percent(a) / 100 )
cost(a)      = -log2(relative(a))  +  0.5 * -log2(absolute(a))
effort(game) = Σ cost(a) over locked achievements
```

where `clamp(x) = min(max(x, 0.001), 1)` keeps both ratios in `(0.001, 1]` so
the logarithm never sees 0.

Games are sorted by ascending `effort`.

Normalizing against the game's own maximum removes the "bought but never
launched" distortion: global percentages are computed across all owners,
including those who never started the game, which makes raw percentages
incomparable between titles. The relative term alone gives a game's most
common achievement cost 0, and grows logarithmically — half as common (within
the game) adds one.

The absolute term is a second, half-weighted cost computed against a fixed
100% ceiling instead of the game's own maximum. It exists specifically to
handle the case in 8.2 below, where the relative term alone gives a wrong
answer. See 8.2 for why it is there before considering removing it.

Mandatory implementation details:

- `percent` can be exactly 0; without clamping the logarithm goes to infinity.
- Global percentages may be missing entirely (fresh releases) — fall back to
  equal weights and label rarity as unknown.
- A missing percentage and a percentage of zero mean different things and must
  never be conflated. Unknown rarity gets a neutral cost; a verified zero is
  genuinely maximally rare.

### 8.1 No blocker detection (decided 2026-07-26)

An earlier version of this design flagged achievements below a rarity
threshold as "blockers" and labelled such games "100% questionable". That
concept is removed deliberately, and should not be reintroduced without
solving the underlying problem first.

A low global percentage does not mean an achievement is hard or unobtainable.
It conflates at least three unrelated causes:

- **When the achievement was added.** Achievements are frequently added years
  after release, especially for older games. By then most owners have stopped
  playing, so the percentage stays permanently low regardless of difficulty.
- **Whether it happens organically.** Many achievements are rare simply
  because nobody stumbles into them while playing normally, yet take twenty
  minutes once you deliberately go for them.
- **Genuine unobtainability.** Dead multiplayer modes and broken triggers —
  the case the flag was meant to catch — are only a fraction of the low
  percentages.

Since a single number cannot separate these, the flag was a noisy heuristic
presented to the user as a fact. Erring in that direction is worse than
silence: it tells someone to abandon a game they would have finished.

Rarity is still used for **cost**, which answers "roughly how much effort",
not "this is impossible". That interpretation survives the objection.

The right way to surface genuine unobtainability is curated per-achievement
information, not inference from statistics — see 10.2.

### 8.2 Why relative rarity alone is not enough (decided 2026-07-26)

The first version of this formula used only the relative term. Running it
against a real 396-game library exposed a defect in the design, not the
implementation: normalizing purely against a game's own maximum collapses
when a game's achievements are all similarly rare, because a narrow spread of
percentages is itself evidence that almost nobody played the game — not
evidence that it is easy.

Observed on the live library:

- **Overture** — 4 achievements, global rarity 2.1%-2.2%. Every achievement
  was ~1.0 relative to the game's own maximum, so total effort came out to
  0.07: ranked **#1 easiest** across 396 games.
- **Rust - Staging Branch** — 6 achievements, 3.7%-3.8%, effort 0.15, ranked
  #2.
- **Schedule I** — 13 achievements spanning 5.6%-95.9%, already 10/13
  unlocked (77% complete), effort 11.02, ranked #14.

A game nobody has played, whose every achievement is held by only 2% of
owners, outranked a game the user had already taken to 77% completion. Pure
relative normalization cannot distinguish "this achievement is common because
the game is easy" from "this achievement is uncommon like everything else in
this barely-touched game" — both look like "average for this game" once
divided by the in-game maximum.

The absolute term (weighted at 0.5, see the `AbsoluteRarityWeight` constant
in `EffortCalculator`) fixes this without discarding the relative term's
value: it charges every achievement for how rare it is *globally*, so a
uniformly-rare game can no longer look free just because nothing in its own
narrow band stands out. Half weight keeps the relative term dominant — it is
still what makes costs comparable across games of different sizes and
audiences — while being enough to push Overture and Rust far down the
ranking. **Do not simplify this back to pure relative normalization**; that
regresses exactly the defect above.

## 9. Screens

**Two ranked lists, not one.** The main screen's "what should I complete
next" question and "what should I start" question are different questions
with different answers, so they get separate lists rather than one queue with
a filter flag or a progress multiplier. On the live 396-game library, 13 of
the top 20 games by the old single-queue ranking had 0% progress — those were
suggestions to *start* a game, not to *finish* one, and drowned out the games
that were actually close to done.

- **Finish what you started** (primary list, shown first) — games with at
  least one unlocked achievement and at least one remaining, ascending by
  remaining effort.
- **Start something new** — games with zero unlocked achievements, same
  sorting.

Fully completed games (nothing remaining) appear in neither list; they are
counted in the summary instead.

Each list is a virtualized list of cards: cover art (`library_600x900.jpg`),
title, progress (`37 of 44`), remaining effort, and — crucially — a "why it is
here" line such as *"3 left: two common, one rare (2.1%)"*. Without that
explanation any recommendation list reads as guesswork. Filters: minimum
playtime, title search.

Rarity is shown as a number, not as a verdict. The user sees "one rare (2.1%)"
and draws their own conclusion; the app does not tell them whether that is
achievable (see 8.1).

**Game screen.** `header.jpg` banner, progress, playtime. The "remaining"
section is sorted by cost, cheapest first; the "unlocked" section by unlock
date. Hidden achievements are shown as hidden: for `hidden: 1` Steam usually
returns an empty description, and there is nowhere to source it in the MVP.

**Sync panel.** Progress, counter, cancel.

**Settings.** Key, account, cache TTLs, database reset.

## 10. Explicitly out of scope for the MVP

### 10.1 Separating base game and DLC achievements

Verified: this does not exist as a concept in Steam's data. Witcher 3 (292030)
exposes 78 achievements for the base game — exactly `achievements.total` — and
none of its 22 DLC appids (`355880`, `378648`, `378649`, …) have achievement
pages at all. Stellaris (281990) exposes 219 achievements in one flat list
where DLC achievements carry no marker. Neither `GetSchemaForGame` nor
`store/appdetails` returns a DLC flag.

Not handled in any way in the MVP. Only `sort_order` and `first_seen_at` are
collected as raw material for a future heuristic.

Accepted consequence: a game like Stellaris will show a low completion
percentage even when everything obtainable without buying DLC is unlocked.

### 10.2 Curated achievement information (far future)

The honest answer to "is this achievement still obtainable, and how do I get
it?" is human-written information, not a statistic. Possible shapes, in
increasing cost: Markdown files in this repository describing specific games
or achievements; a shared server serving the same content; community
contributions.

This would also be the right home for genuine unobtainability data — a curated
"this achievement is broken / requires dead servers" note carries authority
that a rarity threshold never can (see 8.1).

Explicitly far future. Recorded here so the reasoning is not lost, not as a
commitment.

### 10.3 Other non-goals

- Full library grid and statistics dashboard — they grow from the same data as
  extra queries, and come after the primary scenario works.
- Trend charts — data accumulates in `snapshots` from day one, the screen
  comes later.
- Friend comparison, multiple accounts.
- Code signing: an unsigned `.exe` triggers SmartScreen. A certificate costs
  $200-400 per year and does not pay off for an MVP — explained in the README
  instead.

## 11. CI and distribution

```
test:           ubuntu-latest    dotnet test Core.Tests       ~40 s
build-windows:  windows-latest   dotnet publish -r win-x64    ~3 min
                                 → artifact, on tags → Release
```

Tests run on Linux because they never touch Windows APIs and the cheaper
runner is faster. The build runs only on `windows-latest` because of WPF.

Published as self-contained single-file: ~80-100 MB versus ~5 MB for
framework-dependent, but users do not need to install the .NET Desktop
Runtime. Under a "everyone downloads it themselves" model, an extra runtime
step costs more than a hundred megabytes.

Trimming stays off: Blazor and WPF rely on reflection, and a trimmed build
fails at runtime in ways only discoverable on Windows through CI.

WebView2 is usually already present on Win10/11 (it ships with Edge), but the
installer must account for its bootstrapper — otherwise some users will just
see an empty window.

## 12. Testing

`ISteamApiClient` lives in Core with a typed `HttpClient` implementation.
Tests substitute an `HttpMessageHandler` that replays recorded fixtures.

Fixtures are captured once on Windows with a real key, anonymized (`steamid`,
`key`) and committed under `testdata/`, alongside real `loginusers.vdf` and
`appmanifest_*.acf` samples for the VDF parser tests.

This makes the following verifiable on macOS: the VDF parser, classification
of every API error, the sync scheduler, the ranking formula, and database
migrations.
