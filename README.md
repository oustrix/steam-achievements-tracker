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

> **Status: early development.** Nothing here is usable yet — the design is
> settled and implementation is starting. Watch the repo if you want the first
> release.

## The problem

Steam tells you a completion percentage and stops there. It will happily show
two games at 90% when one needs a single evening and the other needs a
co-op partner who stopped playing in 2016.

Percentage alone is a bad guide, because the number of achievements left and
how hard they are matter more than the ratio. This tool ranks your library by
**how much work is actually left**, not by how far along the bar looks.

## How the ranking works

Every locked achievement gets a cost derived from its global rarity,
normalized against the most common achievement in that same game:

```
relative(a)  = percent(a) / max(percent in this game)
cost(a)      = -log2(relative(a))
effort(game) = sum of cost over locked achievements
```

That normalization matters. Steam computes global percentages across
**everyone who owns a game, including people who never launched it** — so a
tutorial achievement can sit at 40% and mean nothing. Comparing raw
percentages between titles is misleading; comparing them against the game's
own baseline is not.

### What it deliberately does not do

It does not tell you an achievement is "impossible". A low global percentage
looks like difficulty but usually is not: achievements are often added years
after release, when most owners have already stopped playing, and many are rare
only because nobody stumbles into them while playing normally — twenty minutes
if you actually go for them.

One number cannot separate "brutally hard" from "added late" from "nobody
tries". So rarity is shown to you as a number, and the judgement stays yours.
Guessing here and guessing wrong would mean telling you to abandon a game you
would have finished in an evening.

## What it does

- Pulls your full Steam library and achievement progress
- Ranks games by remaining effort, with a plain-language reason for each
  position — *"3 left: two common, one rare (2.1%)"*
- Per-game view: what is left, sorted easiest first, with rarity for each
- Works offline once synced — everything is cached locally in SQLite

## Requirements

- Windows 10 or 11
- A Steam account with **Game details set to Public** (otherwise Steam's API
  returns nothing, even to you)
- A free Steam Web API key — Steam only issues these to accounts with at least
  $5 in purchases

## Getting started

1. Download the latest `.exe` from Releases and run it. No installer, no
   runtime to install.
2. The app finds your SteamID automatically from your local Steam
   installation and asks you to confirm it is you.
3. Click the button to open the API key page — you are already signed in
   there. Copy the key; the app picks it up from your clipboard on its own.
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
dotnet test                  # logic tests, run anywhere
dotnet publish SteamAchievements.Windows -r win-x64 -c Release
```

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

- [Design document](docs/specs/2026-07-26-steam-achievements-tracker-design.md) —
  architecture and the reasoning behind each decision
- [Steam API reference](docs/steam-api.md) — endpoint behaviour verified
  against the live API, including its inconsistent error codes

## License

MIT — see [LICENSE](LICENSE).
