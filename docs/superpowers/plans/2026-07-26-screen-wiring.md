# Screen Wiring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bind every control on the onboarding, sync and settings screens to the services that already exist behind them, so the application is usable end to end.

**Architecture:** Every decision expressible as a function of state moves into `SteamAchievements.Core/Presentation` and is unit-tested on macOS; components keep the markup, the subscription and the service call. The three places that re-read the database when a sync ends share one edge detector rather than three copies of a flag.

**Tech Stack:** .NET 10, Blazor (Razor Class Library), xUnit, SQLite via Dapper, WPF + BlazorWebView as the shipping host, Blazor Server as the macOS preview host.

**Spec:** `docs/superpowers/specs/2026-07-26-screen-wiring-design.md`. Read it before Task 1; it explains *why* for everything below.

## Global Constraints

- **Everything committed is in English** — code, comments, UI strings, commit messages. Non-negotiable, the repository is open source.
- **Never build or run `SteamAchievements.Windows` on macOS.** It targets `net10.0-windows` and does not compile here. A bare `dotnet test` at the repository root fails with NETSDK1100 for the same reason — always name the project.
- **The local feedback loop is `dotnet test SteamAchievements.Core.Tests`.** That plus `dotnet build SteamAchievements.UI` and `dotnet build SteamAchievements.Preview` is the bar for a change being ready to push.
- **Never let `Microsoft.Win32` or `System.Security.Cryptography.ProtectedData` into Core.**
- **Components must wrap every reaction to a service event in `InvokeAsync(StateHasChanged)`** before touching `ILibraryQuery`. `SqliteLibraryQuery` sits on one `SqliteConnection`; concurrent use corrupts rather than throws.
- **Components that subscribe must unsubscribe in `Dispose`.** The services are singletons in the shipping host and outlive any component instance.
- **Run `dotnet format` before each commit.**
- Tests use xUnit with plain `Assert`. No FluentAssertions, no NSubstitute for these types — hand-written fakes, following `SteamAchievements.Core.Tests/Fakes.cs`.

## File Structure

**Created:**

| Path | Responsibility |
|---|---|
| `SteamAchievements.Core/Presentation/NoticeSeverity.cs` | How loud a notice is. Moved from the UI project. |
| `SteamAchievements.Core/Presentation/KeySubmissionMessage.cs` | The four key-submission outcomes as user-facing copy. |
| `SteamAchievements.Core/Presentation/SyncControls.cs` | Sync state → the sync card's title and buttons. |
| `SteamAchievements.Core/Presentation/AccountRowView.cs` | Stored account + mismatch → the account row. |
| `SteamAchievements.Core/Presentation/LibraryChangeSignal.cs` | One edge detector for "the library may have changed". |
| `SteamAchievements.Core.Tests/Presentation/KeySubmissionMessageTests.cs` | |
| `SteamAchievements.Core.Tests/Presentation/SyncControlsTests.cs` | |
| `SteamAchievements.Core.Tests/Presentation/AccountRowViewTests.cs` | |
| `SteamAchievements.Core.Tests/Presentation/LibraryChangeSignalTests.cs` | |
| `SteamAchievements.UI/Shared/ApiKeyForm.razor` (+`.css`) | The key field, used by onboarding and by settings. |
| `SteamAchievements.UI/Shared/SteamIdField.razor` (+`.css`) | Manual SteamID64 entry, used by onboarding and by settings. |
| `SteamAchievements.UI/Onboarding/OnboardingLayout.razor` (+`.css`) | Chromeless layout for onboarding. |
| `SteamAchievements.Preview/Fixtures/FixtureSync.cs` | Replaces `FixtureSyncPresenter`; implements both sync seams and really runs. |
| `SteamAchievements.Preview/Fixtures/FixtureOnboarding.cs` | |
| `SteamAchievements.Preview/Fixtures/FixtureAccountAdmin.cs` | |
| `SteamAchievements.Preview/Fixtures/FixtureLinks.cs` | |

**Modified:** `SteamAchievements.UI/_Imports.razor`, `Shared/Notice.razor` (severity type only), `Onboarding/OnboardingPage.razor`, `Sync/SyncPage.razor`, `Sync/SyncProgressCard.razor`, `Settings/SettingsPage.razor`, `Layout/AppShell.razor`, `Layout/SyncStatusCard.razor`, `Queue/QueuePage.razor`, `Core.Tests/Fakes.cs`, `Preview/Fixtures/FixtureLibraryQuery.cs`, `Preview/Program.cs`, `Windows/App.xaml.cs`.

**Deleted:** `SteamAchievements.UI/Shared/NoticeSeverity.cs`, `SteamAchievements.Preview/Fixtures/FixtureSyncPresenter.cs`.

## Divergences from the spec, decided while planning

Recorded here rather than discovered during implementation:

1. **`SyncControlsView` has no `Force` property** (spec §4.1 lists one). The primary button is `Start(force: false)` in every state it is enabled; `force: true` belongs only to the separate "Full resync" button, which needs no lookup. A property that is always `false` is a field waiting to be misread.
2. **`ApiKeyForm` renders the notice for all four outcomes, including `Accepted`** (spec §3.4 gives `Accepted` to the embedding screen). It still raises `OnAccepted`, which is what onboarding uses to start the sync and navigate; settings simply stays and the form's own notice is the one it wanted. This removes a parameter rather than adding one.

---

### Task 1: Move `NoticeSeverity` into Core

`KeySubmissionMessage` (Task 2) returns a severity, and Core cannot reference `SteamAchievements.UI`. This has to happen first.

**Files:**
- Create: `SteamAchievements.Core/Presentation/NoticeSeverity.cs`
- Delete: `SteamAchievements.UI/Shared/NoticeSeverity.cs`
- Modify: `SteamAchievements.UI/_Imports.razor`

**Interfaces:**
- Produces: `SteamAchievements.Core.Presentation.NoticeSeverity` with members `Info`, `Warning`, `Danger`.

- [ ] **Step 1: Create the Core type**

`SteamAchievements.Core/Presentation/NoticeSeverity.cs`:

```csharp
namespace SteamAchievements.Core.Presentation;

/// <summary>
/// How loud a notice is.
///
/// In Core rather than in the UI project because Core decides it:
/// <see cref="KeySubmissionMessage"/> returns the severity of the message it
/// describes, and Core cannot reference SteamAchievements.UI. A parallel enum
/// on this side plus a mapping in the component would put the same four-way
/// table in two places, which is exactly what that type exists to prevent.
/// </summary>
public enum NoticeSeverity
{
    Info,
    Warning,
    Danger,
}
```

- [ ] **Step 2: Delete the UI copy**

```bash
git rm SteamAchievements.UI/Shared/NoticeSeverity.cs
```

- [ ] **Step 3: Add the usings the later tasks need**

`SteamAchievements.UI/_Imports.razor` — add these two lines after the existing `@using SteamAchievements.Core.Presentation`:

```razor
@using SteamAchievements.Core.App
@using SteamAchievements.Core.Data
```

`SteamAchievements.Core.App` carries `OnboardingStep` and `SteamId`; `SteamAchievements.Core.Data` carries `StoredAccount`. Both are used from `.razor` files in later tasks. `NoticeSeverity` needs no new using — `SteamAchievements.Core.Presentation` is already imported, which is why `Notice.razor` itself does not change.

- [ ] **Step 4: Verify both consumers still compile**

```bash
dotnet build SteamAchievements.UI && dotnet build SteamAchievements.Preview
```

Expected: both succeed. A failure here means some `.cs` file referenced `SteamAchievements.UI.Shared.NoticeSeverity` explicitly — fix by deleting that using, not by restoring the enum.

- [ ] **Step 5: Commit**

```bash
dotnet format
git add -A
git commit -m "refactor: move NoticeSeverity into Core so Core can name it"
```

---

### Task 2: `KeySubmissionMessage`

**Files:**
- Create: `SteamAchievements.Core/Presentation/KeySubmissionMessage.cs`
- Test: `SteamAchievements.Core.Tests/Presentation/KeySubmissionMessageTests.cs`

**Interfaces:**
- Consumes: `NoticeSeverity` (Task 1); `KeySubmission` from `SteamAchievements.Core.Presentation` (exists).
- Produces: `KeyMessage(string Title, string Body, NoticeSeverity Severity)` and `KeySubmissionMessage.For(KeySubmission result) → KeyMessage`.

- [ ] **Step 1: Write the failing tests**

`SteamAchievements.Core.Tests/Presentation/KeySubmissionMessageTests.cs`:

```csharp
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests.Presentation;

public class KeySubmissionMessageTests
{
    [Fact]
    public void MalformedSaysNoRequestWasMade()
    {
        var message = KeySubmissionMessage.For(KeySubmission.Malformed);

        Assert.Equal(NoticeSeverity.Warning, message.Severity);
        Assert.Contains("32", message.Body);
        Assert.Contains("Nothing was sent", message.Body);
    }

    [Fact]
    public void RejectedIsTheOnlyOutcomeThatAsksForAnotherKey()
    {
        var rejected = KeySubmissionMessage.For(KeySubmission.Rejected);
        var unreachable = KeySubmissionMessage.For(KeySubmission.Unreachable);

        Assert.Equal(NoticeSeverity.Danger, rejected.Severity);
        Assert.Contains("another key", rejected.Body);
        Assert.DoesNotContain("another key", unreachable.Body);
    }

    [Fact]
    public void UnreachableAdvisesRetrying()
    {
        var message = KeySubmissionMessage.For(KeySubmission.Unreachable);

        Assert.Equal(NoticeSeverity.Warning, message.Severity);
        Assert.Contains("try again", message.Body);
    }

    [Fact]
    public void AcceptedConfirmsTheKeyWasStored()
    {
        var message = KeySubmissionMessage.For(KeySubmission.Accepted);

        Assert.Equal(NoticeSeverity.Info, message.Severity);
        Assert.Contains("stored", message.Body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A new outcome must not reach a screen as an empty notice or an
    /// exception thrown inside a render.
    /// </summary>
    [Fact]
    public void EveryOutcomeHasCopy()
    {
        foreach (var outcome in Enum.GetValues<KeySubmission>())
        {
            var message = KeySubmissionMessage.For(outcome);

            Assert.NotEqual("", message.Title);
            Assert.NotEqual("", message.Body);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~KeySubmissionMessageTests
```

Expected: build error, `KeySubmissionMessage` does not exist.

- [ ] **Step 3: Write the implementation**

`SteamAchievements.Core/Presentation/KeySubmissionMessage.cs`:

```csharp
namespace SteamAchievements.Core.Presentation;

public sealed record KeyMessage(string Title, string Body, NoticeSeverity Severity);

/// <summary>
/// What the user is told about a submitted key.
///
/// Here rather than in the component because the distinction this table
/// carries is the reason <see cref="IOnboarding.SubmitKeyAsync"/> returns four
/// values instead of a boolean: a key Steam refused needs a different key, and
/// a Steam that could not be reached needs another attempt. Advice that swaps
/// those two sends the user to Steam's website for a key that was already fine.
/// </summary>
public static class KeySubmissionMessage
{
    public static KeyMessage For(KeySubmission result) => result switch
    {
        KeySubmission.Malformed => new KeyMessage(
            "That does not look like a key",
            "A Steam Web API key is 32 hexadecimal characters. Nothing was sent to Steam.",
            NoticeSeverity.Warning),

        KeySubmission.Rejected => new KeyMessage(
            "Steam refused this key",
            "The key was revoked or mistyped. Retrying will not help — issue another key on Steam's key page.",
            NoticeSeverity.Danger),

        KeySubmission.Unreachable => new KeyMessage(
            "Steam could not be reached",
            "The key was not checked and nothing was stored. It may well be fine — try again.",
            NoticeSeverity.Warning),

        _ => new KeyMessage(
            "Key stored",
            "The key is stored, encrypted for this Windows account.",
            NoticeSeverity.Info),
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~KeySubmissionMessageTests
```

Expected: 5 passed.

- [ ] **Step 5: Commit**

```bash
dotnet format
git add SteamAchievements.Core/Presentation/KeySubmissionMessage.cs SteamAchievements.Core.Tests/Presentation/KeySubmissionMessageTests.cs
git commit -m "feat: state what each key submission outcome tells the user"
```

---

### Task 3: `SyncControls`

**Files:**
- Create: `SteamAchievements.Core/Presentation/SyncControls.cs`
- Test: `SteamAchievements.Core.Tests/Presentation/SyncControlsTests.cs`

**Interfaces:**
- Consumes: `SyncStatusView`, `SyncPhase`, `SyncProblem` (all exist).
- Produces: `SyncControlsView(string Title, string PrimaryLabel, bool PrimaryEnabled, bool ShowCancel)` and `SyncControls.For(SyncStatusView status) → SyncControlsView`.

- [ ] **Step 1: Write the failing tests**

`SteamAchievements.Core.Tests/Presentation/SyncControlsTests.cs`:

```csharp
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests.Presentation;

public class SyncControlsTests
{
    private static SyncStatusView Idle(SyncProblem problem = SyncProblem.None) =>
        SyncStatusView.Idle with { Problem = problem };

    [Fact]
    public void IdleOffersASync()
    {
        var controls = SyncControls.For(Idle());

        Assert.Equal("Sync", controls.PrimaryLabel);
        Assert.True(controls.PrimaryEnabled);
        Assert.False(controls.ShowCancel);
    }

    /// <summary>
    /// Retrying cannot help until the key is replaced, and the screen's own
    /// notice links to settings. An enabled button here spends requests to
    /// rediscover what is already known.
    /// </summary>
    [Fact]
    public void ARejectedKeyDisablesTheButton()
    {
        Assert.False(SyncControls.For(Idle(SyncProblem.InvalidKey)).PrimaryEnabled);
    }

    /// <summary>The user may have just made their game details public.</summary>
    [Fact]
    public void APrivateProfileStillOffersARetry()
    {
        Assert.True(SyncControls.For(Idle(SyncProblem.PrivateProfile)).PrimaryEnabled);
    }

    [Fact]
    public void ADifferentAccountDisablesTheButton()
    {
        Assert.False(SyncControls.For(Idle(SyncProblem.OtherAccount)).PrimaryEnabled);
    }

    [Fact]
    public void RunningOffersPauseAndCancel()
    {
        var controls = SyncControls.For(SyncStatusView.Idle with { Phase = SyncPhase.Running });

        Assert.Equal("Pause", controls.PrimaryLabel);
        Assert.True(controls.PrimaryEnabled);
        Assert.True(controls.ShowCancel);
    }

    [Fact]
    public void PausedOffersResume()
    {
        var controls = SyncControls.For(SyncStatusView.Idle with { Phase = SyncPhase.Paused });

        Assert.Equal("Resume", controls.PrimaryLabel);
        Assert.True(controls.PrimaryEnabled);
        Assert.False(controls.ShowCancel);
    }

    [Fact]
    public void AnOpenCircuitDisablesTheButton()
    {
        var controls = SyncControls.For(SyncStatusView.Idle with { Phase = SyncPhase.CircuitOpen });

        Assert.False(controls.PrimaryEnabled);
        Assert.Contains("retry", controls.Title);
    }

    /// <summary>
    /// SyncCoordinator publishes no CircuitOpen today, so most of this grid is
    /// unreachable. The mapping is still total, so that whoever makes one of
    /// these states reachable gets a button rather than an exception thrown
    /// inside a render, on Windows, where it costs a full CI cycle to see.
    /// </summary>
    [Fact]
    public void EveryPhaseAndProblemPairIsMapped()
    {
        foreach (var phase in Enum.GetValues<SyncPhase>())
        {
            foreach (var problem in Enum.GetValues<SyncProblem>())
            {
                var controls = SyncControls.For(
                    SyncStatusView.Idle with { Phase = phase, Problem = problem });

                Assert.NotEqual("", controls.Title);
                Assert.NotEqual("", controls.PrimaryLabel);
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~SyncControlsTests
```

Expected: build error, `SyncControls` does not exist.

- [ ] **Step 3: Write the implementation**

`SteamAchievements.Core/Presentation/SyncControls.cs`:

```csharp
namespace SteamAchievements.Core.Presentation;

/// <summary>
/// What the sync card draws. <c>ShowCancel</c> rather than an enabled flag:
/// cancelling something that is not running is not a disabled action, it is not
/// an action.
/// </summary>
public sealed record SyncControlsView(
    string Title, string PrimaryLabel, bool PrimaryEnabled, bool ShowCancel);

/// <summary>
/// The whole "state → buttons" table, in Core so it is under dotnet test.
///
/// There is no Force here. The primary button is always
/// <c>Start(force: false)</c>; a full resync is its own button and needs no
/// lookup to know what it does.
/// </summary>
public static class SyncControls
{
    public static SyncControlsView For(SyncStatusView status) => status.Phase switch
    {
        SyncPhase.Running => new SyncControlsView("Sync in progress", "Pause", true, true),

        SyncPhase.Paused => new SyncControlsView("Sync paused", "Resume", true, false),

        SyncPhase.CircuitOpen => new SyncControlsView("Sync waiting to retry", "Sync", false, false),

        _ => new SyncControlsView("No sync running", "Sync", CanStart(status.Problem), false),
    };

    // A blocked library is not a library that benefits from another attempt.
    // A private profile is: the setting may have been changed a moment ago.
    private static bool CanStart(SyncProblem problem) => problem switch
    {
        SyncProblem.InvalidKey or SyncProblem.OtherAccount => false,
        _ => true,
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~SyncControlsTests
```

Expected: 8 passed.

- [ ] **Step 5: Commit**

```bash
dotnet format
git add SteamAchievements.Core/Presentation/SyncControls.cs SteamAchievements.Core.Tests/Presentation/SyncControlsTests.cs
git commit -m "feat: put the sync card's buttons under test"
```

---

### Task 4: `AccountRowView`

**Files:**
- Create: `SteamAchievements.Core/Presentation/AccountRowView.cs`
- Test: `SteamAchievements.Core.Tests/Presentation/AccountRowViewTests.cs`

**Interfaces:**
- Consumes: `StoredAccount(ulong SteamId64, string PersonaName, string AvatarUrl)` from `SteamAchievements.Core.Data`; `AccountMismatch(ulong ActiveSteamId64, string ActiveAccountName)` from `SteamAchievements.Core.Presentation`.
- Produces: `AccountRow(string Name, string Detail, string? AvatarUrl, string? SwitchPrompt, ulong? SwitchTarget)` and `AccountRowView.For(StoredAccount? stored, AccountMismatch? mismatch) → AccountRow`.

- [ ] **Step 1: Write the failing tests**

`SteamAchievements.Core.Tests/Presentation/AccountRowViewTests.cs`:

```csharp
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests.Presentation;

public class AccountRowViewTests
{
    private static readonly StoredAccount Stored =
        new(76561190000000001, "someone", "https://avatars.example/1.jpg");

    [Fact]
    public void SaysNobodyIsSignedInBeforeOnboarding()
    {
        var row = AccountRowView.For(stored: null, mismatch: null);

        Assert.Equal(AccountRowView.NoAccount, row.Name);
        Assert.Null(row.AvatarUrl);
        Assert.Null(row.SwitchPrompt);
        Assert.Null(row.SwitchTarget);
    }

    [Fact]
    public void ShowsTheStoredAccount()
    {
        var row = AccountRowView.For(Stored, mismatch: null);

        Assert.Equal("someone", row.Name);
        Assert.Contains("76561190000000001", row.Detail);
        Assert.Equal("https://avatars.example/1.jpg", row.AvatarUrl);
    }

    /// <summary>
    /// A real path, not a defensive branch: ChooseAccountAsync stores an empty
    /// persona name whenever steamcommunity does not answer or the profile is
    /// private, and the row must still name somebody.
    /// </summary>
    [Fact]
    public void FallsBackToTheSteamIdWhenTheProfileNeverAnswered()
    {
        var row = AccountRowView.For(Stored with { PersonaName = "", AvatarUrl = "" }, mismatch: null);

        Assert.Equal("76561190000000001", row.Name);
        Assert.Null(row.AvatarUrl);
    }

    [Fact]
    public void OffersTheActiveAccountWhenSteamDisagrees()
    {
        var row = AccountRowView.For(Stored, new AccountMismatch(76561190000000002, "currentuser"));

        Assert.Equal("someone", row.Name);
        Assert.Contains("currentuser", row.SwitchPrompt);
        Assert.Equal(76561190000000002UL, row.SwitchTarget);
    }

    [Fact]
    public void OffersTheActiveAccountEvenWithNothingStored()
    {
        var row = AccountRowView.For(stored: null, new AccountMismatch(76561190000000002, "currentuser"));

        Assert.Equal(AccountRowView.NoAccount, row.Name);
        Assert.Equal(76561190000000002UL, row.SwitchTarget);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~AccountRowViewTests
```

Expected: build error, `AccountRowView` does not exist.

- [ ] **Step 3: Write the implementation**

`SteamAchievements.Core/Presentation/AccountRowView.cs`:

```csharp
using System.Globalization;
using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Presentation;

/// <summary>
/// The account row on the settings screen. <c>SwitchPrompt</c> and
/// <c>SwitchTarget</c> are both null or both set — an offer to switch needs
/// somebody to switch to.
/// </summary>
public sealed record AccountRow(
    string Name, string Detail, string? AvatarUrl, string? SwitchPrompt, ulong? SwitchTarget);

public static class AccountRowView
{
    public const string NoAccount = "Not signed in yet";

    public static AccountRow For(StoredAccount? stored, AccountMismatch? mismatch)
    {
        var prompt = mismatch is null
            ? null
            : $"Steam is signed in as {mismatch.ActiveAccountName}";

        if (stored is null)
        {
            return new AccountRow(
                NoAccount,
                "Detected from loginusers.vdf during onboarding",
                null,
                prompt,
                mismatch?.ActiveSteamId64);
        }

        var id = stored.SteamId64.ToString(CultureInfo.InvariantCulture);
        var named = stored.PersonaName.Length > 0;

        return new AccountRow(
            named ? stored.PersonaName : id,
            named ? id : $"{id} — the public profile did not answer",
            stored.AvatarUrl.Length > 0 ? stored.AvatarUrl : null,
            prompt,
            mismatch?.ActiveSteamId64);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~AccountRowViewTests
```

Expected: 5 passed.

- [ ] **Step 5: Commit**

```bash
dotnet format
git add SteamAchievements.Core/Presentation/AccountRowView.cs SteamAchievements.Core.Tests/Presentation/AccountRowViewTests.cs
git commit -m "feat: describe the account row without the screen deciding"
```

---

### Task 5: `LibraryChangeSignal`

**Files:**
- Create: `SteamAchievements.Core/Presentation/LibraryChangeSignal.cs`
- Test: `SteamAchievements.Core.Tests/Presentation/LibraryChangeSignalTests.cs`
- Modify: `SteamAchievements.Core.Tests/Fakes.cs` (append two fakes)

**Interfaces:**
- Consumes: `ISyncPresenter`, `IAccountAdmin` (both exist).
- Produces: `LibraryChangeSignal(ISyncPresenter sync, IAccountAdmin accounts)`, `event Action? Changed`, `void Dispose()`. Also `FakeSyncPresenter` (with `Publish(SyncStatusView)`) and `FakeAccountAdmin` (with `Raise()`) in `SteamAchievements.Core.Tests`.

- [ ] **Step 1: Add the fakes**

Append to `SteamAchievements.Core.Tests/Fakes.cs` (it already has `using SteamAchievements.Core.Abstractions;` at the top — add `using SteamAchievements.Core.Data;` and `using SteamAchievements.Core.Presentation;` beside it):

```csharp
/// <summary>
/// An <see cref="ISyncPresenter"/> whose state a test drives directly.
/// <see cref="Publish"/> is the whole point: it sets the status and raises the
/// event in one call, the way SyncCoordinator does.
/// </summary>
public sealed class FakeSyncPresenter : ISyncPresenter
{
    public SyncStatusView Status { get; private set; } = SyncStatusView.Idle;

    public event Action? Changed;

    public void Publish(SyncStatusView status)
    {
        Status = status;
        Changed?.Invoke();
    }
}

/// <summary>
/// An <see cref="IAccountAdmin"/> that records what it was asked to do and
/// raises <see cref="Changed"/> on demand.
/// </summary>
public sealed class FakeAccountAdmin : IAccountAdmin
{
    public StoredAccount? Current { get; set; }

    public AccountMismatch? Mismatch { get; set; }

    public ulong? SwitchedTo { get; private set; }

    public int Resets { get; private set; }

    public event Action? Changed;

    public Task SwitchToAsync(ulong steamId64, CancellationToken cancellationToken)
    {
        SwitchedTo = steamId64;
        Raise();
        return Task.CompletedTask;
    }

    public void ResetEverything()
    {
        Resets++;
        Raise();
    }

    public void Raise() => Changed?.Invoke();
}
```

- [ ] **Step 2: Write the failing tests**

`SteamAchievements.Core.Tests/Presentation/LibraryChangeSignalTests.cs`:

```csharp
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests.Presentation;

public class LibraryChangeSignalTests
{
    private static SyncStatusView Running(int completed) =>
        SyncStatusView.Idle with { Phase = SyncPhase.Running, Completed = completed, Total = 100 };

    [Fact]
    public void RaisesOnceWhenTheSyncStops()
    {
        var sync = new FakeSyncPresenter();
        var accounts = new FakeAccountAdmin();
        using var signal = new LibraryChangeSignal(sync, accounts);

        var raised = 0;
        signal.Changed += () => raised++;

        sync.Publish(Running(1));
        sync.Publish(SyncStatusView.Idle);

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// Progress arrives about five times a second and GetQueue re-ranks the
    /// whole library. Re-reading on each report would run a full ranking pass
    /// under a writing sync and reorder rows beneath the user's cursor.
    /// </summary>
    [Fact]
    public void StaysQuietWhileTheSyncRuns()
    {
        var sync = new FakeSyncPresenter();
        using var signal = new LibraryChangeSignal(sync, new FakeAccountAdmin());

        var raised = 0;
        signal.Changed += () => raised++;

        sync.Publish(Running(1));
        sync.Publish(Running(2));
        sync.Publish(Running(3));

        Assert.Equal(0, raised);
    }

    [Fact]
    public void StaysQuietWhenTheSyncStarts()
    {
        var sync = new FakeSyncPresenter();
        using var signal = new LibraryChangeSignal(sync, new FakeAccountAdmin());

        var raised = 0;
        signal.Changed += () => raised++;

        sync.Publish(Running(1));

        Assert.Equal(0, raised);
    }

    /// <summary>
    /// A switch and a reset both empty the library, and neither goes through a
    /// sync phase at all.
    /// </summary>
    [Fact]
    public void RaisesOnEveryAccountChange()
    {
        var accounts = new FakeAccountAdmin();
        using var signal = new LibraryChangeSignal(new FakeSyncPresenter(), accounts);

        var raised = 0;
        signal.Changed += () => raised++;

        accounts.Raise();
        accounts.ResetEverything();

        Assert.Equal(2, raised);
    }

    [Fact]
    public void StopsListeningAfterDispose()
    {
        var sync = new FakeSyncPresenter();
        var accounts = new FakeAccountAdmin();
        var signal = new LibraryChangeSignal(sync, accounts);

        var raised = 0;
        signal.Changed += () => raised++;

        signal.Dispose();

        sync.Publish(Running(1));
        sync.Publish(SyncStatusView.Idle);
        accounts.Raise();

        Assert.Equal(0, raised);
    }

    /// <summary>
    /// The signal is built while a sync is already in flight — the host
    /// resolves it at startup, and a sync can be running by then only in the
    /// sense that a later construction is legal. Starting from "running" means
    /// the first stop is still an edge.
    /// </summary>
    [Fact]
    public void TakesItsStartingStateFromThePresenter()
    {
        var sync = new FakeSyncPresenter();
        sync.Publish(Running(1));

        using var signal = new LibraryChangeSignal(sync, new FakeAccountAdmin());

        var raised = 0;
        signal.Changed += () => raised++;

        sync.Publish(SyncStatusView.Idle);

        Assert.Equal(1, raised);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~LibraryChangeSignalTests
```

Expected: build error, `LibraryChangeSignal` does not exist.

- [ ] **Step 4: Write the implementation**

`SteamAchievements.Core/Presentation/LibraryChangeSignal.cs`:

```csharp
namespace SteamAchievements.Core.Presentation;

/// <summary>
/// One answer to "the stored library may have changed, re-read it".
///
/// Three screens need it — the queue, the sidebar's summary card and the sync
/// screen's history — and three copies of a "was it running" flag drift. It is
/// in Core rather than in the UI project because it is logic, and logic in the
/// UI project is verified by pushing to CI and running the artifact on Windows.
///
/// It fires on the trailing edge of a run rather than on progress: reports
/// arrive about five times a second and re-reading the queue re-ranks the whole
/// library.
///
/// It does not marshal onto a render thread. That obligation stays with the
/// components, where it belongs — see the host design section 5.3 — because a
/// dispatcher in Core would hide the rule from the only code that can obey it.
/// </summary>
public sealed class LibraryChangeSignal : IDisposable
{
    private readonly ISyncPresenter _sync;
    private readonly IAccountAdmin _accounts;
    private readonly Lock _gate = new();

    private bool _wasRunning;

    public LibraryChangeSignal(ISyncPresenter sync, IAccountAdmin accounts)
    {
        _sync = sync;
        _accounts = accounts;
        _wasRunning = sync.Status.Phase == SyncPhase.Running;

        _sync.Changed += OnSyncChanged;
        _accounts.Changed += OnAccountsChanged;
    }

    public event Action? Changed;

    private void OnSyncChanged()
    {
        bool stopped;

        // The edge is computed under the lock because ISyncPresenter.Changed
        // arrives on the sync engine's worker thread while Start raises it on
        // the caller's; two threads deciding "was it running" from one field is
        // how a run ends without anybody re-reading the library. Raising happens
        // outside, so a handler that reads Status cannot re-enter.
        lock (_gate)
        {
            var running = _sync.Status.Phase == SyncPhase.Running;
            stopped = _wasRunning && !running;
            _wasRunning = running;
        }

        if (stopped)
        {
            Changed?.Invoke();
        }
    }

    private void OnAccountsChanged() => Changed?.Invoke();

    public void Dispose()
    {
        _sync.Changed -= OnSyncChanged;
        _accounts.Changed -= OnAccountsChanged;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test SteamAchievements.Core.Tests --filter FullyQualifiedName~LibraryChangeSignalTests
```

Expected: 6 passed.

- [ ] **Step 6: Run the whole suite**

```bash
dotnet test SteamAchievements.Core.Tests
```

Expected: everything green. Core is now complete for this feature.

- [ ] **Step 7: Commit**

```bash
dotnet format
git add SteamAchievements.Core/Presentation/LibraryChangeSignal.cs SteamAchievements.Core.Tests/Presentation/LibraryChangeSignalTests.cs SteamAchievements.Core.Tests/Fakes.cs
git commit -m "feat: detect the end of a sync once instead of in three screens"
```

---

### Task 6: Preview fixtures for the three seams

Nothing in the UI can be wired until the preview host can resolve these, and the fixtures decide whether this feature is verified on macOS or on Windows. They are therefore interactive rather than static.

**Files:**
- Create: `SteamAchievements.Preview/Fixtures/FixtureSync.cs`
- Create: `SteamAchievements.Preview/Fixtures/FixtureOnboarding.cs`
- Create: `SteamAchievements.Preview/Fixtures/FixtureAccountAdmin.cs`
- Create: `SteamAchievements.Preview/Fixtures/FixtureLinks.cs`
- Delete: `SteamAchievements.Preview/Fixtures/FixtureSyncPresenter.cs`
- Modify: `SteamAchievements.Preview/Fixtures/FixtureLibraryQuery.cs`
- Modify: `SteamAchievements.Preview/Program.cs`

**Interfaces:**
- Consumes: `LibraryChangeSignal` (Task 5).
- Produces: `FixtureLibraryQuery.Cleared` (settable bool), `Scenario.FirstRun`, and the four fixture classes.

- [ ] **Step 1: Let the fixture library be emptied**

In `SteamAchievements.Preview/Fixtures/FixtureLibraryQuery.cs`, add `FirstRun` to the `Scenario` enum, after `OtherAccount`:

```csharp
    // A machine that has never been onboarded: no account, no key. Reaching
    // this state through the fixtures is the only way to see AppShell's
    // onboarding guard on macOS.
    FirstRun,
```

Add the property, next to `Scenario`:

```csharp
    /// <summary>
    /// Set by FixtureAccountAdmin when the user switches accounts or resets.
    /// Both empty the library for real, so the confirmations are verified by
    /// their effect rather than by their appearance.
    /// </summary>
    public bool Cleared { get; set; }
```

And make `Source` respect both — replace its first arm:

```csharp
    private IReadOnlyList<FixtureGame> Source => Cleared ? [] : Scenario switch
    {
        Scenario.Empty or Scenario.PrivateProfile or Scenario.InvalidKey or Scenario.FirstRun => [],
```

Everything else follows: `GetSummary`, `GetQueue` and `GetSyncHistory` already branch on `Source.Count == 0`.

- [ ] **Step 2: Write `FixtureSync`**

`SteamAchievements.Preview/Fixtures/FixtureSync.cs`:

```csharp
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>
/// Both sync seams, the way SyncCoordinator implements both in the real host.
///
/// It genuinely runs: Start advances a timer-driven counter to the end and then
/// publishes Idle. Without a real phase transition there is no way to check on
/// macOS that leaving Running actually refreshes the queue and the sidebar,
/// which is the part of this feature most likely to be wrong.
///
/// The timer fires on the thread pool, so the Changed handlers land off the
/// render thread exactly as they do in production.
/// </summary>
public sealed class FixtureSync : ISyncPresenter, ISyncController, IDisposable
{
    private const int Total = 1482;
    private const int GamesPerTick = 60;
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(120);

    private static readonly string[] Games =
    [
        "Divinity: Original Sin 2", "Hollow Knight", "Slay the Spire",
        "Factorio", "Hades", "Return of the Obra Dinn",
    ];

    private readonly FixtureLibraryQuery _library;
    private readonly Lock _gate = new();

    private Timer? _timer;
    private SyncStatusView? _live;
    private int _completed;

    public FixtureSync(FixtureLibraryQuery library) => _library = library;

    public SyncStatusView Status
    {
        get
        {
            lock (_gate)
            {
                return _live ?? Scenario;
            }
        }
    }

    public event Action? Changed;

    /// <summary>
    /// The states a scenario asks for, shown until somebody presses a button.
    /// After that the fixture's own run is the truth.
    /// </summary>
    private SyncStatusView Scenario => _library.Scenario switch
    {
        Fixtures.Scenario.InvalidKey => SyncStatusView.Idle with { Problem = SyncProblem.InvalidKey },
        Fixtures.Scenario.PrivateProfile => SyncStatusView.Idle with { Problem = SyncProblem.PrivateProfile },
        Fixtures.Scenario.OtherAccount => SyncStatusView.Idle with { Problem = SyncProblem.OtherAccount },

        Fixtures.Scenario.CircuitOpen => new SyncStatusView(
            SyncPhase.CircuitOpen, 412, Total, "Divinity: Original Sin 2 — schema",
            "~6 min left", "4.8 req/s",
            "Paused after five consecutive failures",
            "Steam returned 429 five times in a row. Waiting 8 s before the next attempt."),

        _ => SyncStatusView.Idle,
    };

    public void Start(bool force)
    {
        lock (_gate)
        {
            if (_live?.Phase == SyncPhase.Running)
            {
                return;
            }

            // Resuming keeps the count; a full resync starts over.
            _completed = force ? 0 : _completed;
            _timer ??= new Timer(_ => Advance(), null, Tick, Tick);
        }

        Advance();
    }

    public void Pause() => Stop(SyncPhase.Paused);

    public void Cancel() => Stop(SyncPhase.Idle);

    private void Stop(SyncPhase phase)
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;

            _live = phase == SyncPhase.Paused
                ? SyncStatusView.Idle with { Phase = phase, Completed = _completed, Total = Total }
                : SyncStatusView.Idle;

            if (phase == SyncPhase.Idle)
            {
                _completed = 0;
            }
        }

        Changed?.Invoke();
    }

    private void Advance()
    {
        lock (_gate)
        {
            _completed = Math.Min(Total, _completed + GamesPerTick);

            if (_completed >= Total)
            {
                _timer?.Dispose();
                _timer = null;

                // A finished run makes the library non-empty again, which is
                // what the queue and the sidebar are meant to notice.
                _library.Cleared = false;
                _live = SyncStatusView.Idle;
            }
            else
            {
                _live = new SyncStatusView(
                    SyncPhase.Running, _completed, Total,
                    $"{Games[_completed / GamesPerTick % Games.Length]} — achievements",
                    "~6 min left", "4.8 req/s", null, null);
            }
        }

        Changed?.Invoke();
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
```

- [ ] **Step 3: Write `FixtureOnboarding`**

`SteamAchievements.Preview/Fixtures/FixtureOnboarding.cs`:

```csharp
using SteamAchievements.Core.App;
using SteamAchievements.Core.Local;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>
/// Onboarding without Steam and without a network.
///
/// The four outcomes of a key submission are reachable by what is typed, and
/// the rule is printed under the field rather than left to be found in this
/// file: 32 hexadecimal characters is accepted, "reject" is refused, "offline"
/// is unreachable, anything else is malformed.
/// </summary>
public sealed class FixtureOnboarding : IOnboarding
{
    public const string RejectTrigger = "reject";
    public const string UnreachableTrigger = "offline";

    public const ulong FixtureSteamId = 76561190000000001;

    private readonly FixtureLibraryQuery _library;

    private ulong? _chosen;
    private bool? _keyStored;

    public FixtureOnboarding(FixtureLibraryQuery library) => _library = library;

    /// <summary>
    /// Every scenario except first-run represents a machine already past
    /// onboarding — without that, AppShell's guard would send every screen in
    /// the preview to /onboarding.
    ///
    /// Read on each access rather than captured in the constructor: the scenario
    /// is set by ScenarioScope while the page renders, which is after the
    /// container has built this. A constructor reading it sees Normal every
    /// time, and ?scenario=first-run would silently do nothing.
    /// </summary>
    public OnboardingStep Step => OnboardingState.Evaluate(
        _chosen ?? (_library.Scenario == Scenario.FirstRun ? null : FixtureSteamId),
        _keyStored ?? _library.Scenario != Scenario.FirstRun);

    public IReadOnlyList<SteamAccount> DiscoveredAccounts =>
    [
        new SteamAccount(76561190000000001, "someone", "Someone", MostRecent: true, FixtureData.Now),
        new SteamAccount(76561190000000002, "otherperson", "Other Person", MostRecent: false, FixtureData.Now.AddDays(-30)),
    ];

    public event Action? Changed;

    public Task ChooseAccountAsync(ulong steamId64, CancellationToken cancellationToken)
    {
        _chosen = steamId64;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public async Task<KeySubmission> SubmitKeyAsync(string pasted, CancellationToken cancellationToken)
    {
        // Long enough to see the controls disabled, short enough not to be a
        // wait. The preview is read as much as it is clicked.
        await Task.Delay(400, cancellationToken);

        var trimmed = pasted.Trim();

        if (trimmed.Equals(RejectTrigger, StringComparison.OrdinalIgnoreCase))
        {
            return KeySubmission.Rejected;
        }

        if (trimmed.Equals(UnreachableTrigger, StringComparison.OrdinalIgnoreCase))
        {
            return KeySubmission.Unreachable;
        }

        if (!ApiKey.TryNormalize(trimmed, out _))
        {
            return KeySubmission.Malformed;
        }

        _keyStored = true;
        Changed?.Invoke();
        return KeySubmission.Accepted;
    }
}
```

`SteamAccount` is `(ulong SteamId64, string AccountName, string PersonaName, bool MostRecent, DateTimeOffset Timestamp)`. `FixtureData.Now` is the frozen clock the rest of the preview already uses.

- [ ] **Step 4: Write `FixtureAccountAdmin` and `FixtureLinks`**

`SteamAchievements.Preview/Fixtures/FixtureAccountAdmin.cs`:

```csharp
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>
/// Switching and resetting really empty the fixture library, so the two-step
/// confirmations are verified by their effect rather than by their appearance.
/// </summary>
public sealed class FixtureAccountAdmin : IAccountAdmin
{
    private readonly FixtureLibraryQuery _library;

    private StoredAccount? _switched;
    private bool _reset;

    public FixtureAccountAdmin(FixtureLibraryQuery library) => _library = library;

    /// <summary>
    /// Derived on each access, not captured in the constructor: ScenarioScope
    /// sets the scenario while the page renders, after the container has built
    /// this. See FixtureOnboarding.Step for the same reason at more length.
    /// </summary>
    public StoredAccount? Current => _reset
        ? null
        : _switched ?? (_library.Scenario == Scenario.FirstRun
            ? null
            : new StoredAccount(FixtureOnboarding.FixtureSteamId, "someone", ""));

    public AccountMismatch? Mismatch => _library.Scenario == Scenario.OtherAccount && Current is not null
        ? new AccountMismatch(76561190000000002, "otherperson")
        : null;

    public event Action? Changed;

    public Task SwitchToAsync(ulong steamId64, CancellationToken cancellationToken)
    {
        _library.Cleared = true;
        _reset = false;
        _switched = new StoredAccount(steamId64, "otherperson", "");

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public void ResetEverything()
    {
        _library.Cleared = true;
        _switched = null;
        _reset = true;

        Changed?.Invoke();
    }
}
```

`SteamAchievements.Preview/Fixtures/FixtureLinks.cs`:

```csharp
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>
/// Nothing leaves the preview host. The last URL is kept so a screen can show
/// what it would have opened — on macOS there is no Steam client to open it in
/// anyway.
/// </summary>
public sealed class FixtureLinks : IExternalLinks
{
    public string? LastUrl { get; private set; }

    public void OpenApiKeyPage() => OpenUrl("https://steamcommunity.com/dev/apikey");

    public void OpenDataFolder() => OpenUrl("(the data folder)");

    public void OpenUrl(string url) => LastUrl = url;
}
```

- [ ] **Step 5: Register everything**

In `SteamAchievements.Preview/Program.cs`, delete the `FixtureSyncPresenter` registration and add, after the `IUserPreferences` line:

```csharp
builder.Services.AddScoped<FixtureSync>();
builder.Services.AddScoped<ISyncPresenter>(s => s.GetRequiredService<FixtureSync>());
builder.Services.AddScoped<ISyncController>(s => s.GetRequiredService<FixtureSync>());

builder.Services.AddScoped<IOnboarding, FixtureOnboarding>();
builder.Services.AddScoped<IAccountAdmin, FixtureAccountAdmin>();
builder.Services.AddScoped<IExternalLinks, FixtureLinks>();

builder.Services.AddScoped<LibraryChangeSignal>();
```

One registration resolved by two interfaces, exactly as the WPF host does with `SyncCoordinator` — two registrations would give the presenter and the controller separate state and the screen would never see its own sync.

Then delete the old fixture:

```bash
git rm SteamAchievements.Preview/Fixtures/FixtureSyncPresenter.cs
```

- [ ] **Step 6: Verify the preview still builds and runs**

```bash
dotnet build SteamAchievements.Preview
```

Expected: success. Then `dotnet run --project SteamAchievements.Preview`, open http://localhost:5100, confirm the queue still renders, and stop it. Nothing is wired yet — this only proves the container resolves.

- [ ] **Step 7: Commit**

```bash
dotnet format
git add -A
git commit -m "test: make the preview fixtures run instead of pose"
```

---

### Task 7: `ApiKeyForm` and `SteamIdField`

**Files:**
- Create: `SteamAchievements.UI/Shared/ApiKeyForm.razor`, `SteamAchievements.UI/Shared/ApiKeyForm.razor.css`
- Create: `SteamAchievements.UI/Shared/SteamIdField.razor`, `SteamAchievements.UI/Shared/SteamIdField.razor.css`

**Interfaces:**
- Consumes: `IOnboarding`, `IExternalLinks`, `KeySubmissionMessage` (Task 2), `SteamId.TryParse`.
- Produces: `<ApiKeyForm SubmitLabel="..." OnAccepted="..." />` and `<SteamIdField ActionLabel="..." Disabled="..." OnAccepted="..." />`, the latter with `EventCallback<ulong>`.

- [ ] **Step 1: Write `ApiKeyForm`**

`SteamAchievements.UI/Shared/ApiKeyForm.razor`:

```razor
@inject IOnboarding Onboarding
@inject IExternalLinks Links
@implements IDisposable

<div class="row">
    <input class="field num" type="text" autocomplete="off" spellcheck="false"
           placeholder="32 hexadecimal characters"
           disabled="@_busy"
           @bind="_pasted" @bind:event="oninput" />

    <button class="secondary" type="button" disabled="@_busy" @onclick="() => Links.OpenApiKeyPage()">
        Open key page
    </button>

    <button class="primary" type="button" disabled="@(_busy || _pasted.Trim().Length == 0)" @onclick="Submit">
        @(_busy ? "Checking…" : SubmitLabel)
    </button>
</div>

@if (_message is not null)
{
    <Notice Severity="_message.Severity" Title="@_message.Title">
        <Body>@_message.Body</Body>
    </Notice>
}

@code {
    [Parameter] public string SubmitLabel { get; set; } = "Save key";

    /// <summary>
    /// Raised after a key is stored. What happens next differs by screen —
    /// onboarding starts the first sync and navigates away, settings stays —
    /// so the form does not decide it.
    /// </summary>
    [Parameter] public EventCallback OnAccepted { get; set; }

    private readonly CancellationTokenSource _cancellation = new();

    private string _pasted = "";
    private bool _busy;
    private KeyMessage? _message;

    private async Task Submit()
    {
        _busy = true;
        _message = null;

        KeySubmission result;

        try
        {
            result = await Onboarding.SubmitKeyAsync(_pasted, _cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // The component is going away. There is nothing left to draw on.
            return;
        }
        finally
        {
            _busy = false;
        }

        _message = KeySubmissionMessage.For(result);

        if (result == KeySubmission.Accepted)
        {
            _pasted = "";
            await OnAccepted.InvokeAsync();
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
```

`SteamAchievements.UI/Shared/ApiKeyForm.razor.css`:

```css
.row { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; }

.field {
    flex: 1;
    min-width: 240px;
    font-size: 13px;
    padding: 11px 13px;
    border-radius: 8px;
    border: 1px solid var(--border);
    background: var(--bg-raised);
    color: var(--text);
}

.field:focus { outline: none; border-color: var(--border-hover); }
.field:disabled { opacity: 0.55; }

.primary {
    font: inherit;
    font-size: 13px;
    font-weight: 600;
    padding: 11px 16px;
    border-radius: 8px;
    border: 0;
    background: var(--accent);
    color: var(--on-accent);
    cursor: pointer;
}

.secondary {
    font: inherit;
    font-size: 13px;
    padding: 11px 14px;
    border-radius: 8px;
    border: 1px solid var(--border);
    background: var(--bg-selected);
    color: var(--text);
    cursor: pointer;
}

.primary:disabled, .secondary:disabled { opacity: 0.55; cursor: default; }
```

- [ ] **Step 2: Write `SteamIdField`**

`SteamAchievements.UI/Shared/SteamIdField.razor`:

```razor
<div class="row">
    <input class="field num" type="text" autocomplete="off" spellcheck="false"
           placeholder="76561190000000000 or a /profiles/ URL"
           disabled="@Disabled"
           @bind="_typed" @bind:event="oninput" />

    <button class="secondary" type="button"
            disabled="@(Disabled || _typed.Trim().Length == 0)" @onclick="Submit">
        @ActionLabel
    </button>
</div>

@if (_error is not null)
{
    <div class="error">@_error</div>
}

@code {
    [Parameter] public string ActionLabel { get; set; } = "Use this account";

    [Parameter] public bool Disabled { get; set; }

    [Parameter, EditorRequired] public EventCallback<ulong> OnAccepted { get; set; }

    // Names the restriction rather than saying "invalid": a user who pasted a
    // vanity URL has not made a typo, and telling them so sends them looking
    // for one.
    private const string Invalid =
        "Enter a 17-digit SteamID64, or the /profiles/ URL that contains one. Vanity links like /id/name are not supported.";

    private string _typed = "";
    private string? _error;

    private async Task Submit()
    {
        if (!SteamId.TryParse(_typed, out var steamId))
        {
            _error = Invalid;
            return;
        }

        _error = null;
        await OnAccepted.InvokeAsync(steamId);
    }
}
```

`SteamAchievements.UI/Shared/SteamIdField.razor.css`:

```css
.row { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; }

.field {
    flex: 1;
    min-width: 260px;
    font-size: 13px;
    padding: 11px 13px;
    border-radius: 8px;
    border: 1px solid var(--border);
    background: var(--bg-raised);
    color: var(--text);
}

.field:focus { outline: none; border-color: var(--border-hover); }
.field:disabled { opacity: 0.55; }

.secondary {
    font: inherit;
    font-size: 13px;
    padding: 11px 14px;
    border-radius: 8px;
    border: 1px solid var(--border);
    background: var(--bg-selected);
    color: var(--text);
    cursor: pointer;
}

.secondary:disabled { opacity: 0.55; cursor: default; }

.error { font-size: 12px; color: var(--danger-text); text-wrap: pretty; }
```

- [ ] **Step 3: Verify they compile**

```bash
dotnet build SteamAchievements.UI && dotnet build SteamAchievements.Preview
```

Expected: success. Nothing renders them yet.

- [ ] **Step 4: Commit**

```bash
dotnet format
git add SteamAchievements.UI/Shared/ApiKeyForm.razor SteamAchievements.UI/Shared/ApiKeyForm.razor.css SteamAchievements.UI/Shared/SteamIdField.razor SteamAchievements.UI/Shared/SteamIdField.razor.css
git commit -m "feat: add the key and SteamID fields both screens need"
```

---

### Task 8: The onboarding screen

**Files:**
- Create: `SteamAchievements.UI/Onboarding/OnboardingLayout.razor`, `.razor.css`
- Modify: `SteamAchievements.UI/Onboarding/OnboardingPage.razor`, `.razor.css`

**Interfaces:**
- Consumes: `IOnboarding`, `ISyncController`, `<ApiKeyForm>`, `<SteamIdField>` (Task 7), `OnboardingStep`.
- Produces: nothing later tasks depend on.

- [ ] **Step 1: Write the chromeless layout**

`SteamAchievements.UI/Onboarding/OnboardingLayout.razor`:

```razor
@inherits LayoutComponentBase
@inject IUserPreferences Preferences

@* No sidebar. Until onboarding is complete there is nowhere else to go, and
   drawing navigation to Sync and Settings offers doors that are not open. The
   accent still has to be set here: it is a custom property read by every
   component below, and AppShell — which normally declares it — is not in this
   tree. *@
<div class="frame" style="--accent: @(Preferences.Accent ?? AccentPalette.Default)">
    @Body
</div>
```

`SteamAchievements.UI/Onboarding/OnboardingLayout.razor.css`:

```css
.frame {
    min-height: 100vh;
    overflow-y: auto;
    background: var(--bg-page);
    color: var(--text);
}
```

- [ ] **Step 2: Rewrite the page**

`SteamAchievements.UI/Onboarding/OnboardingPage.razor` — replace the whole file:

```razor
@page "/onboarding"
@layout OnboardingLayout
@implements IDisposable
@inject IOnboarding Onboarding
@inject ISyncController Sync
@inject NavigationManager Navigation

<PageTitle>Getting started</PageTitle>

<div class="page">
    <StepIndicator Labels="@(new[] { "account", "key", "first sync" })" Current="@StepNumber" />

    <section class="card">
        <div class="head">
            <span class="title">Is this you?</span>
            <span class="sub">Found in your local Steam installation — nothing was sent anywhere.</span>
        </div>

        @if (_accounts.Count == 0)
        {
            <div class="sub">
                No Steam account was found on this machine. That is normal if Steam is not
                installed or nobody has signed in — enter the SteamID64 instead.
            </div>
        }
        else
        {
            @foreach (var account in _accounts)
            {
                <div class="account @(account.SteamId64 == _chosen ? "chosen" : null)">
                    <div class="avatar"></div>
                    <div class="who">
                        <span class="name">@Display(account)</span>
                        <span class="num id">@account.SteamId64</span>
                    </div>
                    <span class="spacer"></span>
                    <button class="primary" type="button" disabled="@_busy"
                            @onclick="() => Choose(account.SteamId64)">
                        Yes, continue
                    </button>
                </div>
            }
        }

        @if (_manual || _accounts.Count == 0)
        {
            <SteamIdField ActionLabel="Use this account" Disabled="@_busy" OnAccepted="Choose" />
        }
        else
        {
            <button class="link" type="button" @onclick="() => _manual = true">
                Enter a SteamID64 or profile URL instead
            </button>
        }
    </section>

    <section class="card key @(_step == OnboardingStep.ChooseAccount ? "dim" : null)">
        <div class="head">
            <span class="title">Paste your Steam Web API key</span>
            <span class="sub bright">
                The button opens Steam's key page — you are already signed in there. Copy the
                key and paste it here.
            </span>
        </div>

        @if (_step == OnboardingStep.ChooseAccount)
        {
            <div class="sub">Choose an account first — the key is checked against it.</div>
        }
        else
        {
            <ApiKeyForm SubmitLabel="Save key" OnAccepted="KeyAccepted" />
        }

        <div class="sub">
            Steam only issues keys to accounts with at least $5 in purchases. If the page
            refuses, that is why — not a problem with this app.
        </div>
    </section>

    <section class="card dim">
        <span class="title">First sync</span>
        <span class="sub">
            It starts on its own once the key is stored. Roughly 9 minutes for 1 500 games,
            limited by Steam's rate limits. Later syncs take seconds.
        </span>
    </section>
</div>

@code {
    private readonly CancellationTokenSource _cancellation = new();

    private IReadOnlyList<SteamAccount> _accounts = [];
    private OnboardingStep _step;
    private ulong? _chosen;
    private bool _manual;
    private bool _busy;

    private int StepNumber => _step switch
    {
        OnboardingStep.ChooseAccount => 1,
        OnboardingStep.EnterKey => 2,
        _ => 3,
    };

    protected override void OnInitialized()
    {
        _accounts = Onboarding.DiscoveredAccounts;
        _step = Onboarding.Step;

        Onboarding.Changed += HandleChanged;
    }

    private static string Display(SteamAccount account) =>
        account.PersonaName.Length > 0 ? account.PersonaName : account.AccountName;

    // IOnboarding carries the same threading contract as ISyncPresenter, and a
    // component written to it stays correct if the event ever starts arriving
    // off the render thread.
    private void HandleChanged() => InvokeAsync(() =>
    {
        _step = Onboarding.Step;
        StateHasChanged();
    });

    private async Task Choose(ulong steamId64)
    {
        _busy = true;

        try
        {
            await Onboarding.ChooseAccountAsync(steamId64, _cancellation.Token);
            _chosen = steamId64;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            _busy = false;
        }
    }

    // The mockup's third card. The user has an empty library and exactly one
    // useful next action, so it is taken for them — on the screen that shows
    // its progress and carries Pause and Cancel.
    private void KeyAccepted()
    {
        Sync.Start(force: false);
        Navigation.NavigateTo("sync");
    }

    public void Dispose()
    {
        Onboarding.Changed -= HandleChanged;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
```

- [ ] **Step 3: Add the styles the new markup needs**

Append to `SteamAchievements.UI/Onboarding/OnboardingPage.razor.css`:

```css
.account.chosen { border-color: var(--accent); }

/* The dashed clipboard field the mockup showed is gone with the clipboard
   watcher (host design 3.4). Its .watching rule stays unused for now rather
   than being deleted in a wiring commit. */
```

Delete nothing else from that file.

- [ ] **Step 4: Verify by clicking it**

```bash
dotnet build SteamAchievements.UI && dotnet run --project SteamAchievements.Preview
```

Open http://localhost:5100/onboarding?scenario=first-run and check, in order:

1. Two accounts are listed, the step indicator shows step 1.
2. "Yes, continue" moves the indicator to step 2 and reveals the key form.
3. Typing `zzzz` and saving gives the malformed notice.
4. `reject` gives the danger notice about issuing another key.
5. `offline` gives the warning that advises trying again.
6. Thirty-two hex characters (`0123456789abcdef0123456789abcdef`) navigates to the sync screen with a run already in progress.
7. Back at `/onboarding?scenario=first-run`, the "Enter a SteamID64 or profile URL instead" link reveals the field; `https://steamcommunity.com/id/gaben` is refused with the vanity-URL message, and a 17-digit number is accepted.

- [ ] **Step 5: Commit**

```bash
dotnet format
git add SteamAchievements.UI/Onboarding
git commit -m "feat: make onboarding do what its three cards describe"
```

---

### Task 9: The sync screen

**Files:**
- Modify: `SteamAchievements.UI/Sync/SyncProgressCard.razor`, `SteamAchievements.UI/Sync/SyncProgressCard.razor.css`
- Modify: `SteamAchievements.UI/Sync/SyncPage.razor`, `SteamAchievements.UI/Sync/SyncPage.razor.css`

**Interfaces:**
- Consumes: `SyncControls` (Task 3), `LibraryChangeSignal` (Task 5), `ISyncController`.
- Produces: `<SyncProgressCard Status="..." OnPrimary="..." OnCancel="..." />` — both `EventCallback`.

- [ ] **Step 1: Rewrite the card**

`SteamAchievements.UI/Sync/SyncProgressCard.razor` — replace the whole file:

```razor
<div class="card">
    <div class="head">
        <span class="title">@Controls.Title</span>
        @if (Status.Total > 0)
        {
            <span class="num counter">
                @Formatting.Number(Status.Completed) / @Formatting.Number(Status.Total)
            </span>
        }
    </div>

    <ProgressBar Percent="Status.Percent" Height="6" />

    <div class="detail num">
        <span>@(Status.CurrentGame.Length == 0 ? "idle" : $"now: {Status.CurrentGame}")</span>
        <span>@Status.EtaText@(Status.RateText.Length == 0 ? "" : $" · {Status.RateText}")</span>
    </div>

    <div class="actions">
        <button class="primary" type="button" disabled="@(!Controls.PrimaryEnabled)" @onclick="OnPrimary">
            @Controls.PrimaryLabel
        </button>

        @if (Controls.ShowCancel)
        {
            <button class="ghost" type="button" @onclick="OnCancel">Cancel</button>
        }

        <span class="spacer"></span>
        <span class="note">Progress is saved — closing the app is safe</span>
    </div>
</div>

@code {
    [Parameter, EditorRequired] public SyncStatusView Status { get; set; } = default!;

    [Parameter] public EventCallback OnPrimary { get; set; }

    [Parameter] public EventCallback OnCancel { get; set; }

    // Recomputed on every render rather than cached in OnParametersSet: it is a
    // pure function of one parameter, and a cached copy is how the two fall out
    // of step.
    private SyncControlsView Controls => SyncControls.For(Status);
}
```

Note the `Title` switch and the `Disabled` property are gone — both are now `SyncControls`'s answer. `Disabled => Status.Phase == SyncPhase.Idle` was also wrong: it disabled the primary button in exactly the state a sync starts from.

- [ ] **Step 2: Wire the page**

`SteamAchievements.UI/Sync/SyncPage.razor` — replace the `@inject` block, the `SyncProgressCard` line, the first `Notice`, and the `@code` block. The three `SyncProblem` notices in the middle of the file stay exactly as they are.

Header:

```razor
@page "/sync"
@implements IDisposable
@inject ILibraryQuery Library
@inject IClock Clock
@inject ISyncPresenter Sync
@inject ISyncController Controller
@inject LibraryChangeSignal LibraryChanged
@inject NavigationManager Navigation
```

The card and the failure notice:

```razor
    <SyncProgressCard Status="_status" OnPrimary="Primary" OnCancel="() => Controller.Cancel()" />

    @if (_status.Phase == SyncPhase.Idle && _status.Problem == SyncProblem.None)
    {
        <button class="resync" type="button" @onclick="() => Controller.Start(force: true)">
            Full resync
        </button>
    }

    @if (_status.AlertTitle is not null)
    {
        <Notice Severity="NoticeSeverity.Warning" Title="@_status.AlertTitle"
                ActionLabel="Retry now" OnAction="() => Controller.Start(force: false)">
            <Body>@_status.AlertBody</Body>
        </Notice>
    }
```

The code block:

```csharp
@code {
    private SyncStatusView _status = SyncStatusView.Idle;
    private IReadOnlyList<SyncRunView> _history = [];

    protected override void OnInitialized()
    {
        _status = Sync.Status;
        _history = Library.GetSyncHistory(20, Clock.Now);

        Sync.Changed += HandleStatusChanged;
        LibraryChanged.Changed += HandleLibraryChanged;
    }

    // Changed arrives on the sync engine's worker thread. Reading ILibraryQuery
    // from there is concurrent use of a single SqliteConnection, which corrupts
    // rather than throws — InvokeAsync is what serializes it.
    private void HandleStatusChanged() => InvokeAsync(() =>
    {
        _status = Sync.Status;
        StateHasChanged();
    });

    private void HandleLibraryChanged() => InvokeAsync(() =>
    {
        try
        {
            _history = Library.GetSyncHistory(20, Clock.Now);
        }
        catch (SqliteException)
        {
            // A re-read racing the write that has just finished. The previous
            // history stays on screen and the next signal tries again; letting
            // it out tears down the circuit and leaves an empty window.
            return;
        }

        StateHasChanged();
    });

    private void Primary()
    {
        if (_status.Phase == SyncPhase.Running)
        {
            Controller.Pause();
            return;
        }

        Controller.Start(force: false);
    }

    private void OpenSettings() => Navigation.NavigateTo("settings");

    public void Dispose()
    {
        Sync.Changed -= HandleStatusChanged;
        LibraryChanged.Changed -= HandleLibraryChanged;
    }
}
```

`SqliteException` needs `@using Microsoft.Data.Sqlite` at the top of the file — `SteamAchievements.UI` already references the package transitively through Core; if the build disagrees, add `Microsoft.Data.Sqlite` to `SteamAchievements.UI.csproj` rather than catching `Exception`.

- [ ] **Step 3: Style the new button**

Append to `SteamAchievements.UI/Sync/SyncPage.razor.css`:

```css
.resync {
    align-self: flex-start;
    font: inherit;
    font-size: 12px;
    padding: 0;
    border: 0;
    background: transparent;
    color: var(--text-muted);
    text-decoration: underline;
    cursor: pointer;
}

.resync:hover { color: var(--text-secondary); }
```

- [ ] **Step 4: Verify by clicking it**

```bash
dotnet run --project SteamAchievements.Preview
```

At http://localhost:5100/sync:

1. Idle shows "No sync running" and an enabled "Sync"; "Full resync" is visible.
2. Pressing "Sync" starts a run: the counter climbs, the title becomes "Sync in progress", the button says "Pause", "Cancel" appears, "Full resync" disappears.
3. "Pause" stops it with the figures still on screen and the button reading "Resume"; "Resume" continues from there.
4. "Cancel" returns to a clean idle with the counter cleared.
5. `?scenario=invalid-key` disables the primary button and shows the rejected-key notice; `?scenario=private-profile` leaves it enabled.

- [ ] **Step 5: Commit**

```bash
dotnet format
git add SteamAchievements.UI/Sync
git commit -m "feat: let the sync screen start, pause and cancel a sync"
```

---

### Task 10: The settings screen

**Files:**
- Modify: `SteamAchievements.UI/Settings/SettingsPage.razor`, `.razor.css`

**Interfaces:**
- Consumes: `IAccountAdmin`, `IOnboarding`, `AccountRowView` (Task 4), `LibraryChangeSignal` (Task 5), `<ApiKeyForm>`, `<SteamIdField>` (Task 7).

- [ ] **Step 1: Rewrite the page**

`SteamAchievements.UI/Settings/SettingsPage.razor` — replace the whole file:

```razor
@page "/settings"
@using SteamAchievements.Core.Sync
@implements IDisposable
@inject IUserPreferences Preferences
@inject ILibraryQuery Library
@inject IClock Clock
@inject IAccountAdmin Accounts
@inject IOnboarding Onboarding
@inject LibraryChangeSignal LibraryChanged

<PageTitle>Settings</PageTitle>

<div class="page">
    <h1>Settings</h1>

    <div class="account">
        @* Not CoverImage: that component is sized by its parent's .cover rule
           and carries a game-cover placeholder. The .avatar rule already in
           this stylesheet is what the mockup wants here. *@
        @if (_row.AvatarUrl is null)
        {
            <div class="avatar"></div>
        }
        else
        {
            <img class="avatar" src="@_row.AvatarUrl" alt="" />
        }
        <div class="who">
            <span class="name">@_row.Name</span>
            <span class="num id">@_row.Detail</span>
        </div>
        <span class="spacer"></span>
        @if (_pending != Pending.Switch)
        {
            <button class="secondary" type="button" @onclick="() => _pending = Pending.Switch">
                Change account
            </button>
        }
    </div>

    @if (_row.SwitchPrompt is not null && _pending != Pending.Switch)
    {
        <Notice Severity="NoticeSeverity.Info" Title="@_row.SwitchPrompt"
                ActionLabel="Switch to it" OnAction="() => _pending = Pending.Switch">
            <Body>
                Cached data belongs to the stored account. Blending two libraries would make
                the ranking meaningless, so switching empties the library first.
            </Body>
        </Notice>
    }

    @if (_pending == Pending.Switch)
    {
        <div class="confirm">
            <span class="warn">
                Switching empties the library — @Formatting.Number(_summary.AchievementCount)
                cached achievements — and keeps the stored key. The next sync starts from nothing.
            </span>

            @if (_row.SwitchTarget is not null)
            {
                <button class="danger" type="button" disabled="@_busy"
                        @onclick="() => Switch(_row.SwitchTarget.Value)">
                    Yes, switch to @_row.SwitchPrompt
                </button>
            }

            <SteamIdField ActionLabel="Switch to this account" Disabled="@_busy" OnAccepted="Switch" />

            <button class="secondary" type="button" @onclick="() => _pending = Pending.None">Cancel</button>
        </div>
    }

    <div class="block">
        <div class="label">Steam Web API key</div>
        @if (_replacingKey)
        {
            <ApiKeyForm SubmitLabel="Replace key" OnAccepted="KeyReplaced" />
        }
        else
        {
            <div class="key-row">
                <div class="num key">@KeyState</div>
                <button class="secondary" type="button" @onclick="() => _replacingKey = true">Replace</button>
            </div>
        }
        <div class="hint">Encrypted with Windows DPAPI for this user account. Never stored in plaintext.</div>
    </div>

    <div class="block">
        <div class="label">Appearance</div>
        <AccentPicker Selected="@Accent" OnSelect="Choose" />
        <div class="hint">Changes the accent used for effort figures, progress and selection.</div>
    </div>

    <div class="block">
        <div class="label">Cache</div>
        <CacheRow Name="Achievement schema" Note="Rarely changes after release"
                  Value="@($"{SyncOptions.Default.SchemaTtl.TotalDays:0} days")" />
        <CacheRow Name="Global rarity percentages" Note="Drifts slowly as more people play"
                  Value="@($"{SyncOptions.Default.GlobalTtl.TotalDays:0} days")" />
        <div class="hint">These are the sync engine's own decisions and are shown, not edited.</div>
    </div>

    <div class="block">
        <div class="label">Local data</div>
        @if (_pending == Pending.Reset)
        {
            <Notice Severity="NoticeSeverity.Danger" Title="Reset database">
                <Body>
                    This cannot be undone. @Formatting.Number(_summary.AchievementCount) cached
                    achievements, every snapshot and the stored key will be deleted.
                </Body>
            </Notice>
            <div class="confirm">
                <button class="secondary" type="button" @onclick="() => _pending = Pending.None">Cancel</button>
                <button class="danger" type="button" @onclick="Reset">Yes, reset</button>
            </div>
        }
        else
        {
            <Notice Severity="NoticeSeverity.Danger" Title="Reset database" ActionLabel="Reset"
                    OnAction="() => _pending = Pending.Reset">
                <Body>
                    Deletes @Formatting.Number(_summary.AchievementCount) cached achievements and all
                    snapshots. The next sync starts from nothing.
                </Body>
            </Notice>
        }
    </div>
</div>

@code {
    /// <summary>
    /// Which destructive action is one click from happening. Two-step in place
    /// rather than a modal: a dialog owns a focus trap, Escape and scroll
    /// locking, none of which can be verified anywhere but Windows.
    /// </summary>
    private enum Pending { None, Switch, Reset }

    private readonly CancellationTokenSource _cancellation = new();

    private LibrarySummary _summary = new(0, 0, "", "");
    private AccountRow _row = AccountRowView.For(null, null);
    private Pending _pending = Pending.None;
    private bool _replacingKey;
    private bool _busy;

    private string Accent => Preferences.Accent ?? AccentPalette.Default;

    private string KeyState => Onboarding.Step == OnboardingStep.Ready
        ? "Stored, encrypted for this Windows account"
        : "Not stored yet";

    protected override void OnInitialized()
    {
        Read();

        Accounts.Changed += HandleAccountsChanged;
        LibraryChanged.Changed += HandleLibraryChanged;
    }

    private void Read()
    {
        _summary = Library.GetSummary(Clock.Now);
        _row = AccountRowView.For(Accounts.Current, Accounts.Mismatch);
    }

    private void HandleAccountsChanged() => InvokeAsync(() =>
    {
        _row = AccountRowView.For(Accounts.Current, Accounts.Mismatch);
        StateHasChanged();
    });

    private void HandleLibraryChanged() => InvokeAsync(() =>
    {
        try
        {
            Read();
        }
        catch (SqliteException)
        {
            return;
        }

        StateHasChanged();
    });

    private void Choose(string accent) => Preferences.SetAccent(accent);

    private async Task Switch(ulong steamId64)
    {
        _busy = true;

        try
        {
            await Accounts.SwitchToAsync(steamId64, _cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            _busy = false;
            _pending = Pending.None;
        }
    }

    // Nothing to navigate to: clearing the stored account makes the step
    // ChooseAccount, and AppShell's guard carries the user to onboarding.
    private void Reset()
    {
        _pending = Pending.None;
        Accounts.ResetEverything();
    }

    private void KeyReplaced() => _replacingKey = false;

    public void Dispose()
    {
        Accounts.Changed -= HandleAccountsChanged;
        LibraryChanged.Changed -= HandleLibraryChanged;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
```

Add `@using Microsoft.Data.Sqlite` at the top if the build asks for it, as in Task 9.

- [ ] **Step 2: Style the confirmation row**

Append to `SteamAchievements.UI/Settings/SettingsPage.razor.css`:

```css
.confirm {
    display: flex;
    flex-direction: column;
    gap: 10px;
    padding: 14px;
    border: 1px solid var(--danger-border);
    border-radius: 9px;
    background: var(--danger-bg);
}

.confirm .warn { font-size: 13px; color: var(--danger-text); text-wrap: pretty; }

.danger {
    align-self: flex-start;
    font: inherit;
    font-size: 13px;
    font-weight: 600;
    padding: 11px 14px;
    border-radius: 8px;
    border: 1px solid var(--danger-button-border);
    background: var(--danger-button);
    color: var(--danger-text);
    cursor: pointer;
}

.danger:disabled { opacity: 0.55; cursor: default; }
```

- [ ] **Step 3: Verify by clicking it**

At http://localhost:5100/settings:

1. The account row names the fixture account and its SteamID64.
2. "Change account" opens the confirmation; "Cancel" closes it without changing anything.
3. Entering a 17-digit id and confirming empties the queue — check `/` — and the account row now names the new account.
4. `?scenario=other-account` shows the "Steam is signed in as otherperson" notice with a one-click switch.
5. "Replace" reveals the key form; `reject` shows the danger notice and the row does not change.
6. "Reset" asks for confirmation, and confirming lands on `/onboarding` — the guard from Task 11 is not in place yet, so before that task the reset simply empties the screen. Re-check this step after Task 11.

- [ ] **Step 4: Commit**

```bash
dotnet format
git add SteamAchievements.UI/Settings
git commit -m "feat: let settings change the account, the key and the database"
```

---

### Task 11: The shell guard and the two remaining refreshes

**Files:**
- Modify: `SteamAchievements.UI/Layout/AppShell.razor`
- Modify: `SteamAchievements.UI/Queue/QueuePage.razor`

**Interfaces:**
- Consumes: `LibraryChangeSignal` (Task 5), `IOnboarding`, `IAccountAdmin`, `OnboardingState`.

- [ ] **Step 1: Add the guard and the summary refresh**

`SteamAchievements.UI/Layout/AppShell.razor` — replace the injects and the `@code` block; the markup does not change:

```razor
@inherits LayoutComponentBase
@implements IDisposable
@inject ILibraryQuery Library
@inject IUserPreferences Preferences
@inject IClock Clock
@inject IOnboarding Onboarding
@inject IAccountAdmin Accounts
@inject LibraryChangeSignal LibraryChanged
@inject NavigationManager Navigation
```

```csharp
@code {
    private LibrarySummary _summary = new(0, 0, "", "");

    protected override void OnInitialized()
    {
        _summary = Library.GetSummary(Clock.Now);

        Preferences.Changed += HandlePreferencesChanged;
        LibraryChanged.Changed += HandleLibraryChanged;

        // Two subscriptions, not one: these are different objects raising
        // different events, and ResetEverything raises only the second. A shell
        // listening to onboarding alone would miss exactly the case this guard
        // exists for — the database reset while the application is running.
        Onboarding.Changed += HandleStateChanged;
        Accounts.Changed += HandleStateChanged;

        Guard();
    }

    /// <summary>
    /// The start path is chosen once, at process start. This covers everything
    /// after that. It is safe from looping because OnboardingPage declares its
    /// own layout and is therefore never inside this component.
    /// </summary>
    private void Guard()
    {
        if (Onboarding.Step != OnboardingStep.Ready)
        {
            Navigation.NavigateTo(OnboardingState.OnboardingRoute.TrimStart('/'));
        }
    }

    private void HandleStateChanged() => InvokeAsync(Guard);

    private void HandlePreferencesChanged() => InvokeAsync(StateHasChanged);

    private void HandleLibraryChanged() => InvokeAsync(() =>
    {
        try
        {
            _summary = Library.GetSummary(Clock.Now);
        }
        catch (SqliteException)
        {
            return;
        }

        StateHasChanged();
    });

    // Load-bearing, not cleanup for its own sake: these services are singletons
    // in the shipping host, so they outlive any one AppShell instance. A page
    // that declares its own @layout destroys and recreates AppShell, and each
    // recreation would otherwise leave the previous instance's handlers
    // attached.
    public void Dispose()
    {
        Preferences.Changed -= HandlePreferencesChanged;
        LibraryChanged.Changed -= HandleLibraryChanged;
        Onboarding.Changed -= HandleStateChanged;
        Accounts.Changed -= HandleStateChanged;
    }
}
```

- [ ] **Step 2: Refresh the queue**

`SteamAchievements.UI/Queue/QueuePage.razor` — add `@inject LibraryChangeSignal LibraryChanged` to the injects, then extend `OnInitialized` and `Dispose`:

```csharp
    protected override void OnInitialized()
    {
        _queue = Library.GetQueue(Clock.Now);
        State.CriteriaChanged += Refresh;
        State.SelectionChanged += Rerender;
        LibraryChanged.Changed += HandleLibraryChanged;
        Refresh();
    }

    // Re-reads once when a sync ends or the account changes, never during a
    // run: GetQueue re-ranks the whole library, and reordering rows under the
    // user's cursor five times a second is worse than being briefly stale.
    private void HandleLibraryChanged() => InvokeAsync(() =>
    {
        try
        {
            _queue = Library.GetQueue(Clock.Now);
        }
        catch (SqliteException)
        {
            return;
        }

        Refresh();
    });
```

and in `Dispose`, alongside the two existing unsubscriptions:

```csharp
        LibraryChanged.Changed -= HandleLibraryChanged;
```

`Refresh` ends with `StateHasChanged()` already, which is why `HandleLibraryChanged` does not call it a second time.

- [ ] **Step 3: Verify the guard and the refresh**

```bash
dotnet run --project SteamAchievements.Preview
```

1. http://localhost:5100/?scenario=first-run redirects to `/onboarding`, and so do `/sync` and `/settings` with the same query.
2. Without the scenario, every screen renders normally — the guard does not fire.
3. Start a sync from `/sync`, then navigate to `/` while it runs: the queue does not thrash. When the run finishes, the sidebar's "last sync" line and the queue's counts update without a navigation.
4. From `/settings`, confirm a reset: the shell carries you to `/onboarding`.

- [ ] **Step 4: Commit**

```bash
dotnet format
git add SteamAchievements.UI/Layout SteamAchievements.UI/Queue
git commit -m "feat: gate the shell on onboarding and refresh it when a sync ends"
```

---

### Task 12: Register the signal in the WPF host

The only change in a project that cannot be built here. Keep it to one commit so a Windows failure has one obvious cause.

**Files:**
- Modify: `SteamAchievements.Windows/App.xaml.cs`

- [ ] **Step 1: Register and dispose it**

In `Compose`, after the two `ISyncPresenter` / `ISyncController` registrations:

```csharp
        services.AddSingleton<LibraryChangeSignal>();
```

Then, immediately after `_services = services.BuildServiceProvider();`, force it into existence:

```csharp
        // Resolved eagerly: it is a subscriber, and a lazily-created subscriber
        // misses every event raised before the first screen that injects it is
        // drawn. The container disposes it with the provider, which unsubscribes
        // it from the coordinator.
        _ = _services.GetRequiredService<LibraryChangeSignal>();
```

`LibraryChangeSignal` implements `IDisposable` and is created by the container, so `_services.Dispose()` in `OnExit` already disposes it — before the connection loop, which is the order that matters.

- [ ] **Step 2: Verify what can be verified here**

```bash
dotnet test SteamAchievements.Core.Tests
dotnet build SteamAchievements.UI
dotnet build SteamAchievements.Preview
```

Expected: all three succeed. `SteamAchievements.Windows` is not built on macOS — CI is what compiles it, and this line is exactly the kind of change that only CI can check.

- [ ] **Step 3: Commit**

```bash
dotnet format
git add SteamAchievements.Windows/App.xaml.cs
git commit -m "feat: register the library change signal in the host"
```

- [ ] **Step 4: Record the divergences**

Append a short section to `docs/superpowers/specs/2026-07-26-screen-wiring-design.md` titled "Divergences from this spec during implementation", listing anything that turned out differently — starting with the two already known (no `Force` on `SyncControlsView`, `ApiKeyForm` rendering all four notices) plus whatever the tasks above surfaced. The Windows host design has such a section and it is the most useful part of it.

```bash
git add docs/superpowers/specs/2026-07-26-screen-wiring-design.md
git commit -m "docs: record how the wiring diverged from its spec"
```

---

## After the plan

Everything above is verified on macOS. What remains is the Windows pass, which is a separate task against the host design §9.1 checklist and must include, specifically:

- DPAPI storing and reading a real key through the settings screen's "Replace".
- Account discovery through the registry filling the onboarding list.
- "Open key page" actually opening a browser.
- A reset with `VACUUM` while the reader and settings connections are open.
- The `LibraryChangeSignal` registration from Task 12.
