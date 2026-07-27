<h1 align="center">Steam Achievements Tracker</h1>

<p align="center">
  <em>Stop guessing which game to 100% next.</em>
</p>

<p align="center">
  <img alt="platform" src="https://img.shields.io/badge/platform-Windows-0078D4">
  <img alt="dotnet" src="https://img.shields.io/badge/.NET-10-512BD4">
  <img alt="license" src="https://img.shields.io/badge/license-MIT-green">
  <img alt="status" src="https://img.shields.io/badge/status-early%20development-orange">
</p>

> **Status: early development — no usable release yet.** The engine is done and
> tested: it reads your local Steam install, syncs your library and achievement
> progress from the Steam Web API into SQLite, and ranks games by remaining
> effort. What is missing is the part you can actually look at — the window, the
> onboarding and the screens. Watch the repo if you want the first release.

## The problem

Steam tells you a completion percentage and stops there. It will happily show
two games at 90% when one needs a single evening and the other needs a
co-op partner who stopped playing in 2016.

Percentage alone is a bad guide, because the number of achievements left and
how hard they are matter more than the ratio. This tool ranks your library by
**how much work is actually left**, not by how far along the bar looks.

## How the ranking works

Every locked achievement is priced by how rare it is, measured two ways at once:

```
relative = percent / (highest percent in this same game)
absolute = percent / 100

cost     = -log2(relative) + 0.5 × (-log2(absolute))
```

A game's effort is the sum over its locked achievements. Lower is easier.

Both halves are needed. Steam computes rarity across **everyone who owns a game,
including people who never launched it**, so raw percentages measure popularity
as much as difficulty — hence the relative term, which compares each achievement
against its own game's easiest. But relative alone collapses when a game's
achievements are all similarly rare: measured on a real library, a game whose
every achievement is held by 2% of owners scored as the **easiest of 396 games**,
because there was no crowd to normalize against. The absolute term fixes that.

You get two lists, because "what should I finish?" and "what should I start?"
are different questions: **finish what you started** (something already
unlocked) and **start something new**.

It never tells you an achievement is impossible — a low percentage usually means
the achievement was added years after release, or that nobody stumbles into it
by accident, not that it is hard. One number cannot separate those, so rarity is
shown to you and the judgement stays yours.

**→ [The full reasoning, with real numbers, is in `docs/ranking.md`](docs/ranking.md)** —
why each term exists, why the weight is ½, what was tried and removed, and the
known limitations.

## What it does

- Pulls your full Steam library and achievement progress
- Two ranked lists — *finish what you started* and *start something new* — each
  with a plain-language reason for every position, such as
  *"3 left, rarest 5.6%"*
- Per-game view: what is left, sorted easiest first, with rarity for each
- Works offline once synced — everything is cached locally in SQLite

## Requirements

- Windows 10 or 11
- The Microsoft Edge WebView2 runtime. It ships with Microsoft Edge and is
  already present on almost every up-to-date Windows installation; if it is
  missing, the application says so on startup and links to the installer
  rather than showing an empty window. It can also be installed ahead of time
  from <https://developer.microsoft.com/microsoft-edge/webview2/>.
- A Steam account with **Game details set to Public** (otherwise Steam's API
  returns nothing, even to you)
- A free Steam Web API key — Steam only issues these to accounts with at least
  $5 in purchases

## Getting started

1. Download the latest release archive, unpack it anywhere, and run
   `SteamAchievements.Windows.exe`. No installer, and no .NET runtime to
   install — everything the application needs is inside that executable. The
   `wwwroot` folder beside it holds the interface's stylesheets and fonts and
   has to travel with it.
2. The app finds your SteamID automatically from your local Steam
   installation and asks you to confirm it is you.
3. Click the button to open the API key page — you are already signed in
   there. Copy the key and paste it into the field; the app checks it against
   Steam before storing it.
4. First sync takes a while for large libraries (roughly 9 minutes for 1500
   games, limited by Steam's rate limits). Later syncs take seconds.

> Windows will show a SmartScreen warning on first launch, because the
> executable is not code-signed — a certificate costs a few hundred dollars a
> year, which is hard to justify for a free tool. Click **More info → Run
> anyway**.

## Privacy

There is no server. Nothing about you leaves your machine.

Your API key is encrypted with Windows DPAPI, scoped to your user account, and
is never written to disk in plaintext. All library and achievement data lives
in a local SQLite database. The app talks to exactly two hosts: Steam's own
API and Steam's image CDN.

## Building from source

```bash
dotnet test tests/SteamAchievements.Core.Tests    # logic tests, run anywhere
dotnet publish src/SteamAchievements.Windows -r win-x64 -c Release   # Windows only
```

Always name the test project: a bare `dotnet test` at the repository root fails
on non-Windows hosts, because the solution includes the `net10.0-windows` WPF
project.

The Windows host uses WPF and therefore only builds on Windows; everything
else — API client, sync engine, ranking, storage — is platform-agnostic and
tested on macOS and Linux. See `CLAUDE.md` for the layout rules.

## Roadmap

Deliberately out of scope for the first release, in rough order of likelihood:

- Full library grid and a statistics dashboard
- Trend charts over time (history is already being recorded from day one)
- Separating base game achievements from DLC — [harder than it sounds](docs/steam-api.md#dlc),
  Steam exposes no DLC flag anywhere in its API
- Friend comparison
- Curated per-achievement notes and guides, which is the only honest way to
  answer "is this still obtainable?" — far future, see the
  [design doc](docs/specs/2026-07-26-steam-achievements-tracker-design.md)

## Documentation

- [How the ranking works](docs/ranking.md) — the formula, the reasoning
  behind each term, and what was deliberately left out
- [Design document](docs/specs/2026-07-26-steam-achievements-tracker-design.md) —
  architecture and the reasoning behind each decision
- [Steam API reference](docs/steam-api.md) — endpoint behaviour verified
  against the live API, including its inconsistent error codes

## License

MIT — see [LICENSE](LICENSE).
