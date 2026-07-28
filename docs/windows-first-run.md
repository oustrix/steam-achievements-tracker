# Windows first run

The application has never run on Windows. This is the single deliberate pass
that establishes whether the host layer works, rather than five fishing trips.

Build: push, then download the `SteamAchievementsTracker-win-x64` artifact from
the run's Actions page. Unpack anywhere and run `SteamAchievements.Windows.exe`.
Data lands in `%LOCALAPPDATA%\SteamAchievementsTracker\`: `library.db`,
`apikey.bin` and `log.txt`.

Work down the list. For each item, note what happened and what `log.txt` said.
The messages quoted below are copied from the source, not paraphrased — a
`findstr` for the quoted text should find the line verbatim (format
placeholders like `{Elapsed}` become numbers; everything else matches).

## The artifact

- [ ] `publish/` holds `SteamAchievements.Windows.exe` and a `wwwroot` tree,
      and no loose `.dll` or `.pdb` anywhere under it, and no `.exe` other than
      the host's — the recursive CI check.

## Starting up

- [ ] The window opens and draws the queue, rather than an empty window.
      **Log:**
      `starting version=… os=… arch=… process=…`,
      `webview2 runtime …` (or `not installed`),
      `folder=… database=… exists=False log=…`,
      `three connections open and the schema migrated in …ms`,
      `steam installation: …` (or `not found`),
      `onboarding step at startup: …`,
      `window shown, start path …`.
- [ ] The RCL's static assets arrived: fonts, `app.css`, the per-component
      isolated CSS, `queue-scroll.js`. A fully working but completely unstyled
      window means the isolated-CSS bundle is named after the wrong assembly
      (`SteamAchievements.Windows.styles.css`, not `Preview`'s).
- [ ] The placeholder gives way to the WebView instead of staying up.
- [ ] If composition itself fails (a corrupt database, a locked file), the
      failure placard appears instead of a blank window.
      **Log:** `composition failed; showing the failure placard`, at Critical,
      with the exception attached.

## Onboarding

- [ ] The registry is read and the Steam account is found.
      **Log:** `steam installation: <path>`, then, once an account is chosen,
      `account chosen steam_id=…`.
- [ ] The API key page opens in a browser
      (`https://steamcommunity.com/dev/apikey`).
- [ ] A key is accepted and stored.
      **Log:** `key submission outcome: Accepted` — and **no key anywhere in
      the file.** Search it for the key's own text before going further;
      finding it is a leaked credential, not a cosmetic bug.
- [ ] DPAPI round-trip: close the application, reopen it, confirm the key is
      still there.
      **Log:** `stored key: present, 32 characters` (logged the first time
      anything asks `LiveSyncRunner` to sync, i.e. at the first sync attempt
      after restart, not at startup).

## The first sync

- [ ] A full sync of the real library completes.
      **Log:** `sync started steam_id=… force=…`,
      `plan: N of M owned games need work (force=…)`,
      a `game <appid> synced` line per game at Debug — roughly 1500 of them,
      from four worker threads; the ledger flags this volume as unmeasured
      before this run, so note whether it is noticeably slow —
      `sync completed games=N in …ms`.
- [ ] Pausing mid-sync leaves the counter and the progress bar on screen. The
      detail line reads "idle" — `CurrentGame`, `EtaText` and `RateText` are
      cleared on pause by design; `Completed` and `Total` are not.
      **Log:** `sync pause requested`, then
      `sync paused after N of M games`.
- [ ] Resuming picks up where it stopped rather than starting over — the next
      `plan: …` line should show a smaller "of M owned games need work" than
      the interrupted run did.
- [ ] **Closing the application during a running sync.** The provider is
      disposed in `OnExit` while the coordinator is mid-run, and the screens'
      change handlers can fire against a renderer that is already gone.
      **Log:** `shutting down`, `services disposed`,
      `N connections closed; goodbye`.
      A `shutdown timed out waiting five seconds for the sync to stop` line
      (Error, from `SyncCoordinator.Dispose`) means the orchestrator's workers
      were still live when the connections closed — a real defect, not a slow
      machine.

## Destructive paths

- [ ] **`VACUUM` inside a reset with three live connections.** Settings →
      Reset database, on a full library. This passes single-connection tests;
      WAL behaves differently with live neighbours, and this has never run.
      **Log:** `reset requested; the library and the stored key will be
      deleted` (Warning), `library emptied in …ms`,
      `vacuum finished in …ms`, `reset finished`. If the vacuum figure is
      large or the operation fails, that is the finding this item exists for.
- [ ] Switching accounts empties the library and keeps the stored key.
      **Log:** `account switch requested from … to …; the library will be
      emptied` (Warning), the same `library emptied` / `vacuum finished` pair,
      `account switch finished steam_id=…`.

## The log itself

- [ ] `log.txt` exists in `%LOCALAPPDATA%\SteamAchievementsTracker\`.
- [ ] Settings → "Open log" opens it, and "Open data folder" opens the folder.
- [ ] The file is readable while the application is still running — it is
      opened `FileMode.Append` with `FileShare.ReadWrite` for exactly this.
- [ ] It rotates: after enough syncs push it past 2 MB, `log.1.txt` appears,
      and the folder never holds more than `log.txt` plus `log.1.txt` through
      `log.3.txt` (four files, eight megabytes, by default).
- [ ] A forced crash — End Task — leaves the last line intact rather than
      truncated. Every write flushes immediately, so this should hold
      regardless of when the crash lands.

## Known gaps, so they are not reported as bugs

- `SyncCoordinator` publishes only `SyncProblem.InvalidKey`. The
  "private profile" and "different account" notices on the sync screen are
  reachable only through the preview's fixtures and cannot appear here.
- `SyncPhase.CircuitOpen` is never published either, so the sync screen's
  "waiting to retry" state is likewise unreachable in the shipping
  application.
- Per-game `LogDebug` calls run roughly 1500 times per sync across four
  worker threads. This is deliberate (§2 of the diagnostics design sets the
  floor to log everything, always) but its real-world cost was never
  measured before this run — that measurement is this checklist's job, not a
  reason to disable it.
