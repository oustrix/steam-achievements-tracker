# Screen Wiring — Design

Date: 2026-07-26

Binds the sync, settings and onboarding screens to the services that already
exist behind them. This is the task the Windows host design deferred in its
§12: "the screens' buttons are still inert".

Companion documents:

- `docs/specs/2026-07-26-steam-achievements-tracker-design.md` — the product.
- `docs/superpowers/specs/2026-07-26-windows-host-design.md` — the host, the
  seams, and the decisions this design inherits rather than revisits.

## 1. What this covers

Every control on `OnboardingPage`, `SyncPage` and `SettingsPage` is currently
`disabled`, and `SyncPage` reads `ISyncPresenter.Status` once in
`OnInitialized` and never again. `ISyncController`, `IOnboarding` and
`IAccountAdmin` are registered in the composition root and consumed by nothing.

After this work the application is usable end to end: a fresh installation can
choose an account, submit a key, run its first sync, watch it, pause it,
replace a rejected key, switch accounts and reset itself.

Not covered, and each has its own reason:

- **The first run on Windows.** Everything in the host is written but
  unexecuted. That verification pass is its own task, against the checklist in
  the host design §9.1.
- **Distribution** — a release job, version stamping, code signing.
- **Vanity URL resolution**, which needs an endpoint absent from
  `docs/steam-api.md`.

## 2. Decisions inherited, not revisited

Four questions this design does not reopen, because the host design settled
them:

- **Onboarding gets a chromeless layout, and `AppShell` redirects to
  `/onboarding` when the state is incomplete** (§3.3). The start path alone is
  not enough — it is chosen once, at process start.
- **There is no clipboard watching** (§3.4). The key field is a plain
  `<input>`; Ctrl+V already works. The copy on the onboarding screen still
  promises a clipboard watcher and is corrected here.
- **A rejected key moves the user nowhere** (§3.5). The sync screen states it
  and links to settings; onboarding is not re-entered.
- **"Onboarding is complete" is a pure function of a stored SteamID64 and the
  presence of a key**, and lives in `OnboardingState`.

## 3. Decisions taken during design

### 3.1 Where a screen's decisions live

Anything expressible as a function of state moves into `Core/Presentation` and
is unit-tested; components keep the markup, the subscription and the call.

This is not symmetry for its own sake. `dotnet test` on macOS is the only
local verification this project has; a decision taken inside a `.razor` file
is verified by pushing to CI, downloading an artifact and running it on
another machine. The codebase already makes this cut three times —
`HostStartupDecision`, `OnboardingState.RouteFor`, `SyncProgressReport` — and
each time for the same reason.

The alternative considered and rejected was a view-model class per screen.
`IOnboarding` *is* the onboarding view model; a second one wrapping it would
add a layer that carries no decisions of its own.

### 3.2 One refresh signal, not three edge detectors

Three places re-read the database when a sync finishes: the queue, the
sidebar's summary card, and the sync screen's history list. The same re-read
is needed after a switch and after a reset, which also empty the library.

Three copies of a `_wasRunning` flag drift. The edge detection is therefore one
class, `LibraryChangeSignal`, in `Core/Presentation` — in Core because it is
logic, and logic that would otherwise be untestable:

```csharp
public sealed class LibraryChangeSignal : IDisposable
{
    public LibraryChangeSignal(ISyncPresenter sync, IAccountAdmin accounts);

    /// <summary>Raised once when the sync phase leaves Running, and on every IAccountAdmin change.</summary>
    public event Action? Changed;
}
```

It does not marshal threads and does not touch the database. Components own
both of those, per §7.

**Live refresh during a run was rejected.** Progress arrives about five times a
second and `ILibraryQuery.GetQueue` re-ranks the whole library; re-querying on
each report would run a full ranking pass under a writing sync and reorder rows
beneath the user's cursor.

### 3.3 Confirmation without a modal

Switching accounts and resetting are destructive and must be confirmed. There
is no modal component in the UI project, and adding one means owning a focus
trap, Escape handling and scroll locking — all of which behave differently in
WebView2 and can therefore only be verified on Windows.

Confirmation is two-step and in place instead: the action button is replaced by
"Cancel" and an explicit affirmative, and the copy states the consequence in
figures. `SettingsPage` holds one field, `PendingAction { None, Switch, Reset }`.
No shared component: the two placements differ in layout, and a component
abstracting them would carry a single `bool`.

### 3.4 One key form, used twice

Submitting a key happens during onboarding and again behind "Replace" in
settings. Both are `IOnboarding.SubmitKeyAsync` with the same four outcomes.

`Shared/ApiKeyForm.razor` owns the field, the "Open key page" button, the
in-flight state and the outcome notice; both screens embed it. The duplication
this avoids is not markup — it is the four-way outcome handling, which is the
part that can be got wrong silently.

What happens *after* a key is accepted differs, so the form does not decide it:
it raises `OnAccepted` and the embedding screen acts. Onboarding starts the
first sync and navigates away, so its user never sees the `Accepted` notice;
settings stays put and shows it. The form renders the notice for the other
three outcomes in both places.

The same argument gives `Shared/SteamIdField.razor`: manual SteamID64 entry
appears during onboarding and again behind "Change account", with the same
`SteamId.TryParse` validation and the same error text about vanity URLs.

### 3.5 The first sync starts itself

On `KeySubmission.Accepted`, onboarding calls `ISyncController.Start(force: false)`
and navigates to `/sync`.

The mockup's third onboarding card is titled "First sync", and a user who has
just finished onboarding has an empty library and exactly one useful next
action. Making them find the sync screen to press one more button adds a step
that has no decision in it. The trade accepted: a nine-minute network operation
begins without a dedicated click — mitigated by landing on the screen that shows
its progress and carries Pause and Cancel.

### 3.6 `NoticeSeverity` moves into Core

`KeySubmissionMessage` returns the severity of the notice it describes, and
Core cannot reference `SteamAchievements.UI`. Declaring a parallel tone enum in
Core and mapping it in the component would put the same four-way table in two
places, which is what the type exists to prevent.

`Core/Presentation` already owns the vocabulary of what a screen shows —
`SyncProblem`, `QueueView`, `SyncStatusView` — so a severity word is not out of
place there. `UI/Shared/NoticeSeverity.cs` is deleted and `Notice.razor` binds
to the Core type; `_Imports.razor` already pulls in `SteamAchievements.Core.Presentation`.

### 3.7 A "Full resync" control

`ISyncController.Start(bool force)` has no caller for `force: true` anywhere in
the application. The sync screen gets a secondary "Full resync" button, active
only when idle, next to the primary one.

Without it the parameter is dead code, and the situation it exists for — cached
data that an incremental sync will not correct, because incremental sync skips
games whose playtime has not changed — has no remedy in the UI at all.

## 4. New types in `Core/Presentation`

### 4.1 `SyncControls`

```csharp
public sealed record SyncControlsView(
    string Title, string PrimaryLabel, bool PrimaryEnabled, bool ShowCancel, bool Force);

public static class SyncControls
{
    public static SyncControlsView For(SyncStatusView status);
}
```

| Phase | Problem | Title | Primary | Enabled | Force | Cancel |
|---|---|---|---|---|---|---|
| `Idle` | `None` | "No sync running" | "Sync" | yes | false | hidden |
| `Idle` | `InvalidKey` | "No sync running" | "Sync" | **no** | — | hidden |
| `Idle` | `PrivateProfile` | "No sync running" | "Sync" | yes | false | hidden |
| `Idle` | `OtherAccount` | "No sync running" | "Sync" | **no** | — | hidden |
| `Running` | any | "Sync in progress" | "Pause" | yes | — | shown |
| `Paused` | any | "Sync paused" | "Resume" | yes | false | hidden |
| `CircuitOpen` | any | "Sync waiting to retry" | "Sync" | **no** | — | hidden |

`Title` is in this record rather than left in the card because it is the same
kind of decision, from the same input; splitting them would put half the
mapping under test and half not. The existing wording is kept, except that
`SyncProgressCard`'s "Full sync in progress" becomes "Sync in progress" — the
card cannot know whether the run was incremental, and most runs are.

This replaces the card's current inline logic, which has a defect worth
naming: `Disabled => Status.Phase == SyncPhase.Idle` disables the primary
button in precisely the state where a sync can be started, so today the card
cannot start one at all.

`InvalidKey` disables the button because retrying cannot help until the key is
replaced — the accompanying notice links to settings. `PrivateProfile` does not,
because the user may have just changed their privacy setting. `OtherAccount`
does not offer a sync at all: blending two libraries is the outcome the
condition exists to prevent.

`Resume` is `Start(force: false)`; there is no `Resume` on the interface, and
progress is written per game, so resuming is simply starting again.

**`CircuitOpen` is unreachable today.** `SyncCoordinator` never publishes it.
The mapping is still total and tested across every `SyncPhase` × `SyncProblem`
pair, so that whoever teaches the coordinator to open the circuit gets a button
rather than an `ArgumentOutOfRangeException` inside a render on Windows.

### 4.2 `KeySubmissionMessage`

```csharp
public sealed record KeyMessage(string Title, string Body, NoticeSeverity Severity);

public static class KeySubmissionMessage
{
    public static KeyMessage For(KeySubmission result);
}
```

| Outcome | Severity | Says |
|---|---|---|
| `Malformed` | `Warning` | Not 32 hexadecimal characters. Nothing was sent to Steam. |
| `Rejected` | `Danger` | Steam refused this key. Another key is needed; retrying will not help. |
| `Unreachable` | `Warning` | Steam could not be reached. The key may be fine — try again. |
| `Accepted` | `Info` | Stored, encrypted for this Windows account. |

The distinction between the middle two is the reason `SubmitKeyAsync` returns
four values rather than a boolean (host design §12). Keeping the wording in a
`.razor` file would leave that distinction unverified.

### 4.3 `AccountRowView`

```csharp
public sealed record AccountRow(
    string Name, string Detail, string? AvatarUrl, string? SwitchPrompt, ulong? SwitchTarget);

public static class AccountRowView
{
    public static AccountRow For(StoredAccount? stored, AccountMismatch? mismatch);
}
```

Cases: nothing stored yet; a stored account with a persona name and avatar; a
stored account whose persona name is empty, where the SteamID64 becomes the
name — a real path, because `ChooseAccountAsync` stores an empty string when
steamcommunity does not answer or the profile is private; and either of the
last two with a mismatch, which fills `SwitchPrompt` and `SwitchTarget`.

## 5. The screens

### 5.1 `AppShell`

Subscribes to `IOnboarding.Changed` **and** `IAccountAdmin.Changed`, and
navigates to `/onboarding` whenever `IOnboarding.Step != Ready`.

Two subscriptions rather than one because they are different objects raising
different events: `ResetEverything` raises only `IAccountAdmin.Changed`, and a
shell listening to onboarding alone would miss exactly the case the guard was
written for — the database reset while the application is running.

The sidebar's `SyncStatusCard` re-reads `GetSummary` on `LibraryChangeSignal`.

### 5.2 Onboarding

`OnboardingLayout.razor`, chromeless. Until onboarding is complete there is
nowhere else to go, and drawing navigation to Sync and Settings offers doors
that are not open.

**Step 1 — account.** `DiscoveredAccounts` as selectable rows; "Yes, continue"
calls `ChooseAccountAsync`. An empty list is normal rather than an error — Steam
may not be installed — so manual entry is shown directly in that case, and
otherwise sits behind the existing "Enter a SteamID64 or profile URL instead"
link. The validation error names the vanity-URL restriction explicitly, so a
user pasting `/id/name` does not read it as a typo of their own.

The placeholder SteamID64 currently hard-coded in the markup is deleted.

**Step 2 — key.** `<ApiKeyForm>`, disabled until an account is stored.
`SubmitKeyAsync` throws `InvalidOperationException` when no account has been
chosen, and its own documentation calls that a screen-ordering bug rather than
a user state — so the gate is in code, not only in styling. The clipboard copy
is replaced.

**Step 3 — first sync.** Per §3.5.

Both service calls reach the network. Controls are disabled while one is in
flight, and the component's `CancellationTokenSource` is cancelled in
`Dispose`: a profile lookup outliving the window has nowhere to deliver.

### 5.3 Sync

Injects `ISyncController`. Subscribes to `ISyncPresenter.Changed`, reacts
through `InvokeAsync(StateHasChanged)`, unsubscribes in `Dispose`.
`SyncProgressCard` takes its label, its enabled state and the visibility of
Cancel from `SyncControls.For(status)`; the "Retry now" action on the failure
notice is `Start(force: false)`; "Full resync" is `Start(force: true)`.

History re-reads on `LibraryChangeSignal`.

### 5.4 Settings

The account row renders `AccountRowView.For(...)`. A mismatch adds an
informational notice offering the detected account; "Change account" reveals
`<SteamIdField>` for an arbitrary one. Both paths lead to the same two-step
confirmation and then `SwitchToAsync`, and the confirmation says plainly that
the library is erased and the key kept.

The key row shows "Stored, encrypted with DPAPI" or "Not stored yet", derived
from `IOnboarding.Step`. That is the only accessor available, and deliberately
so: `ISecretStore` is not injected into components, and the key's value is
never rendered. "Replace" reveals the same `<ApiKeyForm>` in place.

"Reset" is the two-step confirmation from §3.3 and then `ResetEverything()`.
`Database.ResetLibrary` clears `steam_id64`, so the step becomes
`ChooseAccount` and `AppShell`'s guard carries the user back to onboarding
without the page arranging it. The accent colour survives: the reset's
`UPDATE settings` names its columns and does not include it.

### 5.5 Queue

Re-reads `GetQueue` on `LibraryChangeSignal`, preserving the current filter and
selection through `QueueState`, which is untouched by this work.

## 6. Composition

`LibraryChangeSignal` is a service and must be registered.

- `SteamAchievements.Windows/App.xaml.cs` — a singleton, resolved eagerly so it
  is subscribed before the first render, and disposed with the provider. This
  is the only change in the WPF project.
- `SteamAchievements.Preview/Program.cs` — scoped, alongside the new fixtures.

## 7. Threading

Unchanged from the host design §5.3, and load-bearing: `ISyncPresenter.Changed`
is raised from the sync engine's worker thread. Every component reacts through
`InvokeAsync(StateHasChanged)` before touching `ILibraryQuery`, because
`SqliteLibraryQuery` sits on a single connection and concurrent use corrupts
rather than throws.

`LibraryChangeSignal` deliberately does not marshal on its subscribers' behalf.
Doing so would need a dispatcher in Core, and would hide the rule from the place
that has to obey it.

## 8. Error handling

The refresh path is the only new place that can fail: a re-query races a sync
that has just finished writing, and `SqliteException` is the realistic outcome.
The handler catches it and leaves the previous data on screen; the next signal
re-queries.

Letting it propagate would tear down the Blazor circuit and leave an empty
window — disproportionate to a game list that is briefly stale.

Nothing else is new. `ChooseAccountAsync` already survives an unreachable
steamcommunity, `SubmitKeyAsync` returns outcomes instead of throwing, and
`SyncCoordinator` already records every failure except the cancellation it
raises itself.

## 9. Preview fixtures

Fully interactive, because that is what decides whether this work is verified
on macOS or on Windows.

- **`FixtureSync`** replaces `FixtureSyncPresenter` and implements both
  `ISyncPresenter` and `ISyncController`, mirroring `SyncCoordinator`. `Start`
  advances `Completed` to the total over a few seconds on a timer; `Pause`
  leaves the figures on screen; `Cancel` returns to a clean idle. Without a
  real phase transition there is no way to see, on macOS, that leaving
  `Running` actually refreshes the queue and the sidebar.
- **`FixtureOnboarding`** branches on the submitted text so all four outcomes
  are reachable without a network: 32 hexadecimal characters → `Accepted`, the
  word `reject` → `Rejected`, the word `offline` → `Unreachable`, anything else
  → `Malformed`. The rule is printed under the field in the preview host, not
  left to be discovered in the source.
- **`FixtureAccountAdmin`** genuinely empties the fixture library on switch and
  on reset, so the confirmations are verified by their effect rather than by
  their appearance.

- **`FixtureLinks`** implements `IExternalLinks` by recording the URL and
  showing it on screen. The preview host does not register `IExternalLinks`
  today, and `ApiKeyForm` injects it — without this the preview fails at
  injection rather than at the button.

**The default fixture state must be `Ready`.** `AppShell`'s guard runs in the
preview host too, and an incomplete default would redirect every screen to
onboarding. A fresh installation is reachable through a new
`?scenario=first-run`, which is also what exercises the guard.

## 10. Testing

Under `dotnet test SteamAchievements.Core.Tests`, on macOS:

- `SyncControls` — the seven rows of §4.1, plus totality across every
  `SyncPhase` × `SyncProblem` pair.
- `KeySubmissionMessage` — four outcomes.
- `AccountRowView` — five cases, including the empty persona name.
- `LibraryChangeSignal` — `Running` → `Idle` raises once; `Running` →
  `Running` does not raise; `Idle` → `Running` does not raise; an
  `IAccountAdmin` change always raises; `Dispose` unsubscribes.

Through `dotnet run --project SteamAchievements.Preview`, the whole path:
`?scenario=first-run` → choose an account → manual SteamID entry → all four key
outcomes → the sync starting by itself → progress, pause, resume, cancel →
back to a queue whose figures have changed → settings: switch with
confirmation, replace the key, reset with confirmation and the redirect it
causes.

Only on Windows: DPAPI storing and reading a real key, account discovery
through the registry, `Process.Start` opening Steam's key page, the
`LibraryChangeSignal` registration in the host, and `VACUUM` inside the reset
with three live connections — already on the host design's outstanding list.

`dotnet format` before committing.

## 11. Risks

- **The guard and the redirect can loop.** `AppShell` redirecting to
  `/onboarding` while `OnboardingPage` uses a different layout is what keeps it
  from re-entering itself. If the onboarding page is ever given the default
  layout, this becomes an infinite navigation loop rather than a visual
  glitch.
- **`Start` during onboarding is fire-and-forget.** `ISyncController.Start` is
  `void` and returns as soon as the run is scheduled; a failure surfaces on the
  sync screen the user is being sent to, which is the intended place, but the
  onboarding page has no way to know the sync did not begin.
- **The two-step confirmation has no timeout.** A pending "Yes, reset" stays
  pending until it is clicked or cancelled. Navigating away discards it,
  because the field lives on the page.
