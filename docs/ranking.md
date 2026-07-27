# How the ranking works

This is the product. Everything else — the API client, the sync engine, the
database — exists to feed this one calculation. It is also the only part of the
project with genuine product uncertainty rather than a right answer, so this
document records not just the formula but why each piece of it survived contact
with a real 396-game library.

## The problem with completion percentage

Steam shows a percentage and stops. Two games at 90% can be an evening apart or
years apart: one needs three achievements most players already have, the other
needs a co-op partner who quit in 2016.

Percentage answers "how far along is the bar". The useful question is "how much
work is left", and that depends on two things percentage discards — how many
achievements remain, and how hard each one is.

## The formula

For every locked achievement:

```
relative = percent / (highest percent in this same game)
absolute = percent / 100

cost     = -log2(relative) + 0.5 × (-log2(absolute))
```

A game's remaining effort is the sum of `cost` over its locked achievements.
Lower is easier. Both terms are clamped so a 0% achievement produces a large
finite cost rather than infinity.

### Why rarity at all

Steam publishes, for every achievement, the share of owners who unlocked it.
That is the only difficulty signal available without a human writing a guide,
and it is free — the endpoint needs no API key.

Logarithms because difficulty is multiplicative, not additive: going from 50%
to 25% of players is the same *kind* of step as going from 4% to 2%. Under
`-log2`, each halving adds exactly 1, so costs stay comparable across the whole
range instead of being dominated by the rare tail.

### Why normalize inside the game (the relative term)

Steam computes those percentages across **everyone who owns the game, including
people who never launched it**. A bundle game that half its owners never
installed will show low percentages for everything, including its tutorial
achievement. Comparing raw percentages between games therefore compares
popularity as much as difficulty.

Dividing by the game's own most-common achievement cancels that. Within a game,
"twice as rare as the easiest one here" means the same thing regardless of how
many owners bounced off it.

### Why that alone was not enough (the absolute term)

Normalization has a blind spot, and it is not theoretical — it made the first
live run useless. Measured against a real library:

| Game | Rarity spread | Effort (relative only) | Rank |
|---|---|---|---|
| Overture | all 4 achievements **2.1–2.2%** | 0.07 | **#1 of 396** |
| Rust - Staging Branch | all 6 achievements **3.7–3.8%** | 0.15 | #2 |
| Schedule I | 5.6–95.9%, already 10/13 done | 11.02 | #14 |

When every achievement in a game is similarly rare, each one sits at ~1.0
relative and costs ~0. So a game whose every achievement is held by 2% of
owners was declared the easiest in the library, while a game the player had
already taken to 77% ranked fourteenth.

Worse, the narrow spread was itself the signal being misread. Overture's
achievements are uniformly rare *because almost nobody played it* — there is no
"easy" achievement to normalize against, because there is no crowd.

Adding the absolute term fixes it without discarding the relative one:

| Game | Before | After |
|---|---|---|
| Overture | 0.07 | 11.11 |
| Rust - Staging Branch | 0.15 | 14.38 |
| Schedule I | 11.02 → rank 14 | **rank 1** in "finish what you started" |

### Why the weight is ½

The two terms answer different questions and neither should silence the other.

At weight 0 the absolute term vanishes and the blind spot above returns. At
weight 1 the absolute term dominates: bundle games where nobody played become
uniformly "hard", which is exactly the popularity-as-difficulty confusion the
relative term exists to remove. One half keeps the relative term as the primary
signal — comparability between games — while making uniform rarity cost real
money.

It is a judgement call, not a derived constant. It is a single named constant
in `EffortCalculator` and changing it is a one-line experiment.

## Two lists, not one

The first live run put games with **0% progress** in 13 of the top 20 slots.
That is mathematically correct — an untouched game with four common
achievements really is cheap — but it answers the wrong question. "What should
I finish?" and "What should I start?" are different questions, so they get
different lists:

- **Finish what you started** — at least one achievement unlocked, at least one
  remaining. This is the primary list.
- **Start something new** — nothing unlocked yet.

Fully completed games appear in neither; they are counted in the summary.

## What this deliberately does not do

**It never claims an achievement is impossible.** An earlier design flagged
achievements below a rarity threshold as "blockers" and labelled such games
"100% questionable". That was removed, and should not come back without solving
the underlying problem, because a low percentage conflates at least three
unrelated causes:

- **When the achievement was added.** Achievements are frequently added years
  after release. By then most owners have stopped playing, so the percentage
  stays low forever regardless of difficulty.
- **Whether it happens organically.** Many achievements are rare only because
  nobody stumbles into them during normal play, yet take twenty minutes once
  you deliberately go for them.
- **Genuine unobtainability.** Dead multiplayer, broken triggers — the case the
  flag was meant to catch, and only a fraction of the low percentages.

One number cannot separate these, so the flag was a noisy heuristic presented as
fact. Erring that way is worse than silence: it tells someone to abandon a game
they would have finished in an evening. Rarity is shown as a number; the
judgement stays with the player.

The honest fix is curated per-achievement notes written by humans, which is
recorded as a far-future direction in the design doc.

## Known limitations

- **Rarity is not difficulty.** It is a proxy, and it conflates the causes
  listed above. The blend mitigates the worst distortion; it does not remove it.
- **DLC achievements are counted with the base game.** Steam exposes no DLC flag
  anywhere in its API — verified, see `docs/steam-api.md`. A game whose
  remaining achievements all require unowned DLC will look merely expensive
  rather than gated behind a purchase.
- **Unknown rarity is treated as neutral,** not as maximally rare. Fresh
  releases have no global percentages yet; guessing "very hard" would bury them
  wrongly.
- **Effort is not time.** It ranks; it does not predict hours.

## Reproducing this yourself

The CLI prints both lists against your own library:

```bash
export STEAM_API_KEY=<your key>
dotnet run --project src/SteamAchievements.Cli -- --steamid <your SteamID64> --top 20
```

The numbers in this document came from exactly that command on a 396-game
library: 339 games with achievements, 146 in progress, 176 not started, 17
fully completed.

The calculation lives in `src/SteamAchievements.Core/Analytics/EffortCalculator.cs`
and is pure — no I/O, no clock, no state — so it can be read in one sitting and
tested exhaustively.
