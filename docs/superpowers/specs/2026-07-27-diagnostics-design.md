# Diagnostics — design

Date: 2026-07-27
Status: approved, not yet implemented

## 1. Why

The application has never run on Windows. Every part of the host layer — DPAPI,
the registry, WebView2, single-file publish, `VACUUM` inside a reset with three
live connections — is written but unexecuted, and a real first sync (roughly
1500 games, nine minutes, four worker threads writing through one SQLite
connection) has never happened through the UI.

Against that, the entire diagnostic surface today is:

- the `sync_runs` table: start time, kind, game count, duration, error text,
  one row per run;
- the startup placard, which carries an exception message;
- "Sync failed" on the sync screen;
- `SteamAchievements.Cli`, which exercises the engine without the UI.

There is no `ILogger`, no `Serilog`, no `Trace`, no `Debug.Write` anywhere in
`Core`, `UI` or `Windows`. Worse, four screens deliberately swallow
`SqliteException` when re-reading, and a rethrow from those handlers reaches
nobody either — they are `InvokeAsync` bodies whose `Task` is never awaited. A
corrupted database is invisible today. No unhandled-exception hook is
installed, so a crash looks like a window that silently disappeared.

This design adds logging so that the first Windows run produces evidence
instead of guesses.

## 2. Decisions

**The seam is `Microsoft.Extensions.Logging.Abstractions`, not a bespoke
interface.** The .NET convention is that libraries accept `ILogger<T>` rather
than inventing their own abstraction. The usual objection here would be the
extra dependency in a 67 MB self-contained executable, and it does not apply:
`Microsoft.Extensions.Logging.Abstractions` 10.0.10 and
`Microsoft.Extensions.Logging` 10.0.0 are already in the WPF host's restored
dependency graph, pulled in by `Microsoft.AspNetCore.Components.WebView.Wpf`.
Referencing the abstractions package from `Core` adds nothing to the artifact.

**The file sink is ours and lives in `Core`, not in `SteamAchievements.Windows`
and not in Serilog.** The boundary rule bans `Microsoft.Win32` and
`ProtectedData` from Core; it does not ban `System.IO`, which Core already uses
in `DataPaths` and `Database`. Putting the writer in Core brings rotation,
formatting, redaction, flushing and failure handling under `dotnet test` on
macOS. That matters more here than elsewhere: the file sink's failure modes —
a locked file, a buffer unflushed at the moment of a crash, a rotation that
loses the newest lines — are precisely the failures this work exists to catch,
and delegating them to a third-party sink configured by a config file would
leave the diagnostic tool's own reliability as the one untested part. Serilog
would also add three genuinely new packages and buffers by default, requiring
`CloseAndFlush` discipline to survive the crash we are trying to record.

**Every level is written, always, with no switch.** The application has never
started on Windows; the first failure must already be in the file, not
reproducible on a second run with a flag set. `LogLevel` appears in each line
so the file can be filtered by eye or by `findstr`, but nothing is dropped.
Raising the floor to `Information` later is a one-line change.

**Redaction happens inside the writer, not at call sites.** A scrubber you have
to remember to call is a scrubber that leaks. Steam's request URLs carry
`key=<32 hex characters>` in the query string, and `SteamApiException` messages
carry those URLs, so the secret can reach the log through paths nobody
inspected. Every formatted line, including the exception block, passes through
`Redaction.Scrub` before it is written.

**Loggers are required constructor parameters, never optional.** An optional
logger that defaults to `NullLogger` turns "the host forgot to wire it up" into
silence, which is the failure mode this repository keeps rediscovering. A
required parameter makes forgetting a compile error. Tests pass
`NullLogger<T>.Instance` explicitly.

One consequence to plan for: `SyncOrchestrator` is not resolved from the
container — `LiveSyncRunner` builds one per run — so `LiveSyncRunner` takes an
`ILoggerFactory` rather than a single logger, and creates the orchestrator's
logger alongside it.

**Volume is not a concern and does not justify an async writer.** A full sync is
about 1500 games, up to three HTTP calls each, roughly 6000 lines and well
under a megabyte. A queue with a background writer would add exactly the
failure this design is built to avoid — lines still in memory when the process
dies — in exchange for throughput nobody needs. One lock around an append with
an explicit flush is correct and simpler.

## 3. `Core/Diagnostics`

### 3.1 `LogFileOptions`

```csharp
public sealed record LogFileOptions(
    string Directory,
    string FileName = "log.txt",
    long MaxBytes = 2 * 1024 * 1024,
    int MaxFiles = 4);
```

`MaxBytes` and `MaxFiles` bound the folder at 8 MB, which is several full syncs
of history and still small enough to attach to an issue.

### 3.2 `LogLine` — formatting, pure

```csharp
public static class LogLine
{
    public static string Format(
        DateTimeOffset at, LogLevel level, string category, string message, Exception? error);

    public static string ShortCategory(string category);
}
```

One line per event:

```
2026-07-27 09:14:02.113Z  DBG  SyncCoordinator  sync started steam_id=76561198000000000 force=False
```

A sortable UTC timestamp with milliseconds, a fixed-width three-letter level
(`TRC DBG INF WRN ERR CRT`), the category, and the message. `ShortCategory`
reduces `SteamAchievements.Core.App.SyncCoordinator` to `SyncCoordinator`; the
namespace is noise once every line carries it.

An exception is appended as its own indented block — type, message, stack —
after the line it belongs to, so a `findstr` for a category still finds the
event and the block travels with it.

### 3.3 `Redaction` — pure

```csharp
public static class Redaction
{
    public static string Scrub(string text);
}
```

Two rules, both format-based, because the writer must not be handed the secret:

1. `key=<value>` and `access_token=<value>` in a query string keep their
   parameter name and lose their value — `key=***`, `access_token=***` —
   matched case-insensitively and consumed up to the next `&` or whitespace.
2. Any standalone run of exactly 32 characters from `[0-9A-F]` becomes `***`.
   That is the shape of a Steam Web API key. Achievement icon URLs carry
   40-character lowercase SHA-1 hashes, which this does not match.

Rule 2 is the safety net for a key that reaches the log without its `key=`
prefix. It cannot be complete — no format rule can — so the call-site rule
stands alongside it: **nothing ever passes an API key to a logger.** The only
code that may mention the key logs whether one is stored and its length.

### 3.4 `RollingFileWriter` — the one stateful part

```csharp
internal sealed class RollingFileWriter : IDisposable
{
    public RollingFileWriter(LogFileOptions options);
    public void Write(string text);
    public bool Disabled { get; }
}
```

- Appends under a single lock, then flushes. The file is therefore complete up
  to the last line at every instant, including immediately before a crash.
- Opened `FileMode.Append` with `FileShare.ReadWrite`, so "Open log" can show
  the file while the application is still writing to it.
- Rotates when the pending write would take the file past `MaxBytes`:
  `log.3.txt` is deleted, `log.2.txt` → `log.3.txt`, `log.1.txt` →
  `log.2.txt`, `log.txt` → `log.1.txt`, and a new `log.txt` is opened.
  `MaxFiles` counts `log.txt` itself, so the default of 4 means `log.txt`
  plus `log.1.txt` through `log.3.txt`.
- **Any `IOException` or `UnauthorizedAccessException` sets `Disabled` and the
  writer never tries again.** Logging must never be the reason the application
  fails to start, and a writer that retries on every line would turn a
  permissions problem into a nine-minute stall during a sync.

### 3.5 `RollingFileLoggerProvider`

```csharp
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    public RollingFileLoggerProvider(LogFileOptions options, Func<DateTimeOffset> now);
}
```

Implements `ILoggerProvider` over `RollingFileWriter`. `IsEnabled` is always
true (§2). Scopes are not supported — `BeginScope` returns a no-op disposable,
because nothing in this application nests work in a way a scope would clarify.
The `Func<DateTimeOffset>` follows the existing convention: nothing in Core
reads the clock ambiently, which is what lets the formatting tests assert exact
output.

### 3.6 `LoggingHandler` — HTTP

```csharp
public sealed class LoggingHandler : DelegatingHandler
{
    public LoggingHandler(ILogger<LoggingHandler> log);
}
```

Logs method, URL, status code and elapsed milliseconds for every Steam request,
and logs-and-rethrows on transport failure. A `DelegatingHandler` rather than a
change to `SteamApiClient`: the client has no reason to know its traffic is
observed, the hosts already compose handlers this way in
`SteamAchievements.Cli`, and an inner stub handler makes this fully testable in
Core. The URL reaches the log scrubbed twice over — once by rule 1 of §3.3 and
once by rule 2.

## 4. What is logged

### 4.1 Startup, in `App.OnStartup` and `Compose`

Application version, OS version and architecture, process bitness, the WebView2
runtime version string (or its absence), the resolved data folder, database and
secret paths, whether the database file already existed, whether a key is
stored and its length — never its value, the Steam installation path found in
the registry (or the failure), the resolved onboarding step and the start route
handed to `BlazorWebView`.

Each of the three SQLite connections logs when it opens and how long it took;
`Migrate` logs that it ran. Composition failure logs the exception in full
before the placard is built.

### 4.2 The shell guard

`AppShell.Guard()` logs the step it read and the route it chose, and logs the
redirect when it navigates. A blank or unexpected first screen is the single
most likely Windows-only failure, and today nothing records which of the two
route decisions produced it.

### 4.3 Onboarding and account administration

`OnboardingService` logs each step transition and the outcome of a key
submission — accepted, rejected, malformed — never the key. `AccountAdminService`
logs the account switch with both SteamID64 values and logs
`ResetEverything` before and after, because both empty the library.

### 4.4 Sync

`SyncCoordinator` logs start (SteamID64, `force`), every published status
change, pause, cancel, completion with counts and duration, and every branch of
its `catch` chain with the exception attached. It does not log anything about
the key: it never reads one — `LiveSyncRunner` does.

`SyncOrchestrator` logs the plan size against the owned-game count, each game's
outcome — synced, no achievements, error — retries and their delays, the
circuit breaker opening and closing, and every progress report.

`LiveSyncRunner` logs whether a key was found before it builds the orchestrator.

### 4.5 The four swallowed `SqliteException` catches

`AppShell`, `QueuePage`, `SyncPage` and `SettingsPage` filter on
`SqliteErrorCode is 5 or 6` and return. Each logs at `Warning` with the error
code before returning. The two unfiltered catches in `SettingsPage`, around
`SwitchToAsync` and `ResetEverything`, log at `Error` — those set `_writeFailed`
and tell the user something went wrong without recording what.

### 4.6 Handlers whose `Task` nobody awaits

Every `InvokeAsync(...)` change handler in the four screens has its body
wrapped so that an escaping exception is logged rather than lost in an
unobserved `Task`. This is the mechanism that makes §4.5 reliable: today even a
deliberate rethrow from those bodies reaches no one.

### 4.7 Destructive paths and shutdown

`Database.ResetLibrary` logs the row deletions and times `VACUUM` separately,
because `VACUUM` against a file with three live connections is the specific
untested behaviour on the first-run checklist. `App.OnExit` logs each step:
disposing the provider, the result of `SyncCoordinator`'s bounded five-second
wait — including whether it timed out — and each connection closed.

### 4.8 The three hooks, in `App.xaml.cs`

- `DispatcherUnhandledException` — log at `Critical`, mark handled, and show a
  `MessageBox` carrying the exception message and the path to `log.txt`. A
  visible message beats a window that vanished; the user closes the
  application deliberately.

  Deliberately **not** the startup placard. `MainWindow.ShowPlacard` is private
  and, once `BlazorWebView` is in `Root.Children`, putting the placard back on
  top is layout work that macOS cannot verify — a bad trade on the one path
  that only runs when something has already gone wrong. `MessageBox` is one
  call with no layout at all.
- `AppDomain.UnhandledException` — log at `Critical`. The process is going down
  regardless; what makes the record survive is §3.4's flush-per-write, so
  there is nothing extra to do here.
- `TaskScheduler.UnobservedTaskException` — log at `Error` and call
  `SetObserved()`.

`OnStartup` therefore runs in this order: resolve `DataPaths`, create the
folder and the logger factory, install the three hooks, log the §4.1 startup
block, then compose. The only unlogged window is the first two steps, and it
stays unlogged on purpose — they are the steps that decide *where* a log could
be written, so the only failures they have are ones that leave nowhere to
record them. A bootstrap provider buffering in memory to cover two lines would
add a second writer and a drain path for no reachable gain.

## 5. Where the code lives

Everything in §3 and §4 is in `Core` except three things that genuinely need
the host: the log directory (`Environment.GetFolderPath`, already resolved by
`DataPaths`), the WebView2 runtime version string, and the three hooks in §4.8,
which are WPF and AppDomain events. Nothing else moves into
`SteamAchievements.Windows`.

`SteamAchievements.Preview` and `SteamAchievements.Cli` log to the console.
Preview is a `WebApplication.CreateBuilder` host, so a console provider is
already registered and all it needs is a `Debug` floor; the CLI builds a
factory by hand. Preview matters because it means every call site added by this
work is exercised on macOS rather than first executed on Windows; the CLI
matters because it is the tool for isolating the engine from the UI, and it
currently prints nothing about what the engine did.

## 6. Opening the log

`IExternalLinks` gains one method:

```csharp
void OpenLogFile();
```

`ShellLinks` implements it with the same `Process.Start` path as
`OpenDataFolder`, and inherits its existing swallow of `Win32Exception` for a
machine with no association for `.txt`. It needs the log file's path, which
`ShellLinks` does not have today — it is constructed with the data folder
alone, so it gains the file name alongside it. `FixtureLinks` records the call
like its neighbours, as `OpenUrl("(the log file)")`, which `LastLinkStrip`
already displays.

The settings screen gains a row in the "Local data" block carrying **two**
buttons, "Open data folder" and "Open log". Two, because `OpenDataFolder` has
no caller anywhere in the application today: `ApiKeyForm` calls
`OpenApiKeyPage`, the startup placard goes through `OpenUrl`, and nothing at
all reaches `OpenDataFolder`. The method was written for a button that was
never built. Adding the log button next to a button that does not exist would
have quietly left that gap in place, so this closes it — one row, both
actions, and `IExternalLinks` finally fully reachable from the UI.

## 7. Testing

On macOS, under `dotnet test tests/SteamAchievements.Core.Tests`:

- **Formatting** — exact expected line for each level; an exception renders its
  indented block; `ShortCategory` reduces a namespaced category.
- **Redaction** — `key=` in a URL; `key=` inside an exception message; a bare
  32-character uppercase hex token; a 40-character lowercase SHA-1 left intact;
  a message with no secret passed through byte-for-byte.
- **Rotation** — writing past `MaxBytes` produces `log.1.txt`; writing past it
  `MaxFiles` times keeps exactly `MaxFiles` files and discards the oldest; the
  newest lines are in `log.txt`, not in the rotated file.
- **Failure** — a directory that cannot be written sets `Disabled`, throws
  nothing, and a subsequent `Write` does not retry.
- **Concurrency** — N threads writing M lines each produce N×M complete lines
  with no interleaving inside a line.
- **`LoggingHandler`** — a stub inner handler returning 200 and one throwing;
  assert method, scrubbed URL and status appear, and that the exception is
  rethrown.
- **Call sites** — a recording `ILogger` asserts that a successful sync logs
  start and completion, that a rejected key logs the rejection, and that the
  swallowed `SqliteException` path logs before returning.

The 316 existing tests must still pass. Construction sites in the test project
gain `NullLogger<T>.Instance`.

Beyond `dotnet test`: `dotnet build src/SteamAchievements.UI`, `dotnet build
src/SteamAchievements.Preview`, and a click-through of
`dotnet run --project src/SteamAchievements.Preview` across all eight
scenarios — `normal`, `empty`, `invalid-key`, `private-profile`,
`rarity-unknown`, `other-account`, `circuit-open`, `first-run` — with the
console output read, not merely present. (CLAUDE.md lists only five of the
eight; §8 corrects it.)

## 8. Documentation

**`docs/windows-first-run.md`** — new, and the living home for a checklist that
is currently §9.1 of `docs/superpowers/specs/2026-07-26-windows-host-design.md`
with additions scattered across two more documents. It carries the seven
existing items, and for each one what `log.txt` must contain if it passed. Plus
these, which are new:

- `VACUUM` inside `Database.ResetLibrary` while the reader, writer and settings
  connections are all open. It passes single-connection tests; WAL behaves
  differently with live neighbours.
- Closing the application during a running sync. The provider is disposed in
  `OnExit` while the coordinator is mid-run, and the screens' change handlers
  can fire against a renderer that is already gone.
- The log file itself: that it is created, that it survives a forced crash with
  the last line intact, that it rotates, and that "Open log" opens it.

**`CLAUDE.md`** — the diagnostics facts, and two corrections to "Current
state": it still says the sync, settings and onboarding buttons are not wired,
which stopped being true when that branch merged, and it lists five preview
scenarios where the `Scenario` enum has eight — `normal` and `circuit-open` and
`first-run` are missing, and `first-run` is the only way to see `AppShell`'s
onboarding guard on macOS.

**Recorded, not fixed** — two observations about the sync screen that belong in
writing rather than in code:

- `SyncCoordinator` publishes only `SyncProblem.InvalidKey`. `PrivateProfile`
  and `OtherAccount` are reachable solely through the preview's fixtures, so
  two of the three notices on the sync screen cannot appear in the shipping
  application.
- On pause, `Completed` and `Total` **are** preserved
  (`SyncCoordinator.cs:182`) — the counter and the progress bar stay. What
  blanks is `CurrentGame`, `EtaText` and `RateText`, so the detail line reads
  "idle". An earlier note claimed the figures were lost; they are not, and
  §4.1 of the wiring design promises nothing about them.

## 9. Out of scope

A configurable level or a switch to turn logging off; structured or JSON
output; log scopes; telemetry or any upload; a viewer inside the application;
instrumenting the ranking formula, which is pure and has no failure mode worth
a line.

## 10. Divergences from this spec during implementation

To be recorded during execution rather than reconstructed afterwards.
