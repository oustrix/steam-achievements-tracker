# Windows Host Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the application run — a WPF window hosting the Blazor components, with real data, real sync, onboarding, settings and a single-file artifact.

**Architecture:** Everything that can be decided without a Windows API is decided in `SteamAchievements.Core` and covered by xUnit: path resolution, key format, onboarding state, database reset, the sync journal and the whole sync state machine. `SteamAchievements.Windows` keeps four classes with no logic in them — registry, DPAPI, shell, WebView2 probe — plus a window and a composition root. The sync state machine is testable because it depends on `ISyncRunner`, not on `SyncOrchestrator`.

**Tech Stack:** .NET 10, WPF + `Microsoft.AspNetCore.Components.WebView.Wpf` 10.0.90 (WebView2), Blazor Razor Class Library, SQLite via Dapper, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-26-windows-host-design.md`. Read it before starting. Every "why" in this plan is there.

## Global Constraints

- **Language:** everything committed is English — code, comments, UI strings, commit messages. No exceptions.
- **Platform:** work happens on macOS. `SteamAchievements.Windows` **does not compile here**. Never build or run it locally; Tasks 10–12 are verified only by CI. Never run a bare `dotnet test` or `dotnet build` at the repository root — it fails with NETSDK1100. Always name the project.
- **The only local verification command:** `dotnet test SteamAchievements.Core.Tests`.
- **Type check command:** `dotnet build SteamAchievements.Core`.
- **Boundary rule:** no `Microsoft.Win32`, no `System.Security.Cryptography.ProtectedData`, nothing Windows-only in `Core` or `UI`. Plain file I/O and `System.Xml.Linq` are fine — they are cross-platform.
- **Dapper facts that already cost debugging time:** Dapper cannot map into `ValueTuple` and does not translate `snake_case` to PascalCase — alias every column explicitly (`SELECT app_id AS AppId`). SQLite reports every INTEGER column as `Int64`, and Dapper's record materializer needs an exact CLR type match — row records use `long` and narrow in the projection.
- **Timestamp format:** every timestamp stored in SQLite is `value.ToString("o")`. `GameRepository` already does this everywhere; matching it is what makes `DateTimeOffset.Parse` in `SqliteLibraryQuery` work.
- **`GameRepository` is not thread-safe.** `SyncOrchestrator` serializes every call to it behind a lock. Nothing in this plan changes that or reaches around it.
- **Never commit a real SteamID64 or a real API key.** Test SteamID64 is `76561190000000002`. Fixtures are anonymized before committing.
- **Commit after every task.** Do not batch.

---

## File Structure

**Created in `SteamAchievements.Core/`:**

| File | Responsibility |
|---|---|
| `Abstractions/ISecretStore.cs` | The one secret the app stores |
| `App/DataPaths.cs` | Where the database and the key file live |
| `App/ApiKey.cs` | Is this pasted text a Steam API key |
| `App/SteamId.cs` | Is this pasted text a SteamID64 or a profile URL |
| `App/OnboardingState.cs` | Which onboarding step applies |
| `App/SteamAccountLocator.cs` | Signed-in Steam accounts on this machine |
| `App/SyncCoordinator.cs` | The sync state machine |
| `App/OnboardingService.cs` | Onboarding actions over the pieces above |
| `App/AccountAdminService.cs` | Switching accounts and resetting |
| `Data/IAccountStore.cs` | Identity and key-rejection state |
| `Data/SqliteAccountStore.cs` | Its SQLite implementation |
| `Data/SyncJournal.cs` | Writes `sync_runs` and `settings.last_full_sync_at` |
| `Presentation/SyncStatusView.cs` | `SyncPhase`, `SyncStatusView`, `ISyncPresenter`, `ISyncController` |
| `Presentation/IOnboarding.cs` | What the onboarding screen talks to |
| `Presentation/IAccountAdmin.cs` | What the settings screen talks to |
| `Presentation/IExternalLinks.cs` | Opening a browser or a folder |
| `Steam/SteamCommunityClient.cs` | The public `?xml=1` profile endpoint |
| `Sync/ISyncRunner.cs` | The seam that makes the coordinator testable |
| `Sync/LiveSyncRunner.cs` | Builds a `SyncOrchestrator` from the stored key |

**Modified in `SteamAchievements.Core/`:**

| File | Change |
|---|---|
| `Data/Database.cs` | `settings.key_rejected_at` column, `ResetLibrary` |
| `Data/SqliteLibraryQuery.cs` | Three-state run outcome, a cancelled branch in `Describe` |
| `Presentation/SyncRunView.cs` | `bool Failed` becomes `SyncRunOutcome Outcome` |

**Created in `SteamAchievements.Windows/`:** `RegistrySteamPathProvider.cs`, `DpapiSecretStore.cs`, `ShellLinks.cs`, `WebView2Probe.cs`, `HostStartup.cs`, `Components/Routes.razor`, `Components/_Imports.razor`, `wwwroot/index.html`.

**Modified in `SteamAchievements.Windows/`:** `SteamAchievements.Windows.csproj`, `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`.

**Created in `testdata/`:** `profile_public.xml`, `profile_not_found.xml`.

**Modified:** `.github/workflows/ci.yml`, `README.md`, `CLAUDE.md`, `docs/steam-api.md`, the spec's divergences section.

---

## Task 1: Data paths, key format and the secret contract

Three small pure pieces with no dependencies. They exist first because every later task uses at least one of them.

**Files:**
- Create: `SteamAchievements.Core/Abstractions/ISecretStore.cs`
- Create: `SteamAchievements.Core/App/DataPaths.cs`
- Create: `SteamAchievements.Core/App/ApiKey.cs`
- Test: `SteamAchievements.Core.Tests/App/DataPathsTests.cs`
- Test: `SteamAchievements.Core.Tests/App/ApiKeyTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ISecretStore` with `string? Read()`, `void Write(string secret)`, `void Clear()`. `DataPaths` record with `string Folder`, `string DatabaseFile`, `string SecretFile`, static `DataPaths Resolve(string baseDirectory)`, instance `void EnsureFolderExists()`. `ApiKey.TryNormalize(string? candidate, out string normalized)` returning `bool`.

- [ ] **Step 1: Write the failing tests**

Create `SteamAchievements.Core.Tests/App/DataPathsTests.cs`:

```csharp
using SteamAchievements.Core.App;

namespace SteamAchievements.Core.Tests.App;

public class DataPathsTests
{
    [Fact]
    public void PutsBothFilesInsideOneApplicationFolder()
    {
        var paths = DataPaths.Resolve(Path.Combine("C:", "Users", "someone", "AppData", "Local"));

        Assert.Equal(paths.Folder, Path.GetDirectoryName(paths.DatabaseFile));
        Assert.Equal(paths.Folder, Path.GetDirectoryName(paths.SecretFile));
    }

    [Fact]
    public void NamesTheFolderAfterTheApplication()
    {
        var paths = DataPaths.Resolve("/tmp/base");

        Assert.Equal("SteamAchievementsTracker", Path.GetFileName(paths.Folder));
    }

    [Fact]
    public void NamesTheTwoFiles()
    {
        var paths = DataPaths.Resolve("/tmp/base");

        Assert.Equal("library.db", Path.GetFileName(paths.DatabaseFile));
        Assert.Equal("apikey.bin", Path.GetFileName(paths.SecretFile));
    }

    [Fact]
    public void RejectsAnEmptyBaseDirectory()
    {
        Assert.Throws<ArgumentException>(() => DataPaths.Resolve("   "));
    }

    [Fact]
    public void CreatesTheFolderOnDemandAndDoesNotMindItAlreadyExisting()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var paths = DataPaths.Resolve(root);

            paths.EnsureFolderExists();
            paths.EnsureFolderExists();

            Assert.True(Directory.Exists(paths.Folder));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
```

Create `SteamAchievements.Core.Tests/App/ApiKeyTests.cs`:

```csharp
using SteamAchievements.Core.App;

namespace SteamAchievements.Core.Tests.App;

public class ApiKeyTests
{
    private const string Valid = "0123456789abcdef0123456789ABCDEF";

    [Fact]
    public void AcceptsThirtyTwoHexCharacters()
    {
        Assert.True(ApiKey.TryNormalize(Valid, out var normalized));
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF", normalized);
    }

    [Theory]
    [InlineData("  0123456789abcdef0123456789ABCDEF  ")]
    [InlineData("\n0123456789abcdef0123456789ABCDEF\r\n")]
    [InlineData("\"0123456789abcdef0123456789ABCDEF\"")]
    [InlineData("'0123456789abcdef0123456789ABCDEF'")]
    public void SurvivesWhatAPasteAddsAroundIt(string pasted)
    {
        Assert.True(ApiKey.TryNormalize(pasted, out var normalized));
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF", normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789ABCDE")]
    [InlineData("0123456789abcdef0123456789ABCDEFF")]
    [InlineData("0123456789abcdef0123456789ABCDEG")]
    [InlineData("0123456789abcdef 123456789ABCDEF")]
    public void RejectsAnythingElse(string? candidate)
    {
        Assert.False(ApiKey.TryNormalize(candidate, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: FAIL — the type or namespace name `DataPaths` / `ApiKey` / `App` could not be found.

- [ ] **Step 3: Write the implementation**

Create `SteamAchievements.Core/Abstractions/ISecretStore.cs`:

```csharp
namespace SteamAchievements.Core.Abstractions;

/// <summary>
/// Stores the Steam API key. Implemented on Windows with DPAPI in the
/// <c>CurrentUser</c> scope; kept behind an interface so Core stays free of
/// Windows APIs and testable on any platform.
///
/// There is deliberately no name parameter. The application stores exactly one
/// secret, and a general-purpose store is an invitation to put another one in
/// it.
/// </summary>
public interface ISecretStore
{
    /// <summary>Null when no secret is stored, and also when the stored one cannot be read.</summary>
    string? Read();

    void Write(string secret);

    void Clear();
}
```

Create `SteamAchievements.Core/App/DataPaths.cs`:

```csharp
namespace SteamAchievements.Core.App;

/// <summary>
/// Where the application keeps its data. Everything except asking the operating
/// system for the base directory happens here, so the only part that cannot run
/// under <c>dotnet test</c> is a single call to
/// <c>Environment.GetFolderPath</c> in the host.
///
/// The base directory is <c>%LOCALAPPDATA%</c> in production. That is
/// per-Windows-user, which matches the DPAPI <c>CurrentUser</c> scope the key is
/// stored under: two Windows users get two databases and two keys instead of a
/// shared library nobody but its owner can sync.
/// </summary>
public sealed record DataPaths(string Folder, string DatabaseFile, string SecretFile)
{
    public const string FolderName = "SteamAchievementsTracker";
    public const string DatabaseFileName = "library.db";
    public const string SecretFileName = "apikey.bin";

    public static DataPaths Resolve(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new ArgumentException("A base directory is required.", nameof(baseDirectory));
        }

        var folder = Path.Combine(baseDirectory, FolderName);

        return new DataPaths(
            folder,
            Path.Combine(folder, DatabaseFileName),
            Path.Combine(folder, SecretFileName));
    }

    /// <summary>
    /// Separate from <see cref="Resolve"/> so that resolving stays a pure
    /// function and the one call that touches the disk is visible at the call
    /// site. <c>CreateDirectory</c> is already idempotent.
    /// </summary>
    public void EnsureFolderExists() => Directory.CreateDirectory(Folder);
}
```

Create `SteamAchievements.Core/App/ApiKey.cs`:

```csharp
namespace SteamAchievements.Core.App;

/// <summary>
/// A Steam Web API key is 32 hexadecimal characters. Checking that before
/// spending a request is worth the twenty lines, and doing it here rather than
/// in the screen keeps it under test.
/// </summary>
public static class ApiKey
{
    public const int Length = 32;

    /// <summary>
    /// Accepts only text that is a key <em>in its entirety</em> once the
    /// wrapping a paste tends to add — spaces, newlines, quotes — is removed.
    /// It deliberately does not search for a 32-character run inside a longer
    /// document: that would match MD5 sums and git hashes.
    /// </summary>
    public static bool TryNormalize(string? candidate, out string normalized)
    {
        normalized = string.Empty;

        if (candidate is null)
        {
            return false;
        }

        var trimmed = candidate.Trim().Trim('"', '\'').Trim();

        if (trimmed.Length != Length)
        {
            return false;
        }

        foreach (var character in trimmed)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        // Steam displays keys in upper case; normalizing means a key pasted
        // twice in different cases is recognised as the same key.
        normalized = trimmed.ToUpperInvariant();
        return true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: PASS, with 12 more tests than before.

- [ ] **Step 5: Commit**

```bash
git add SteamAchievements.Core/Abstractions/ISecretStore.cs \
        SteamAchievements.Core/App SteamAchievements.Core.Tests/App
git commit -m "feat: add data paths, API key format and the secret store contract"
```

---

## Task 2: The key-rejection column and the account store

`settings` has held identity columns since the first migration and nothing has ever written them. This task makes them writable and adds the column §3.5 needs.

**Files:**
- Modify: `SteamAchievements.Core/Data/Database.cs` (the `EnsureColumn` calls at the end of `Migrate`)
- Create: `SteamAchievements.Core/Data/IAccountStore.cs`
- Create: `SteamAchievements.Core/Data/SqliteAccountStore.cs`
- Test: `SteamAchievements.Core.Tests/Data/SqliteAccountStoreTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `StoredAccount(ulong SteamId64, string PersonaName, string AvatarUrl)`. `IAccountStore` with `StoredAccount? Current { get; }`, `void Set(ulong steamId64, string personaName, string avatarUrl)`, `DateTimeOffset? KeyRejectedAt { get; }`, `void MarkKeyRejected(DateTimeOffset at)`, `void ClearKeyRejected()`.

- [ ] **Step 1: Write the failing test**

Create `SteamAchievements.Core.Tests/Data/SqliteAccountStoreTests.cs`:

```csharp
using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Tests.Data;

public class SqliteAccountStoreTests
{
    private static readonly ulong SteamId = 76561190000000002;

    [Fact]
    public void ReportsNoAccountBeforeOnboarding()
    {
        using var connection = Database.Open(":memory:");

        Assert.Null(new SqliteAccountStore(connection).Current);
    }

    [Fact]
    public void RoundTripsTheStoredAccount()
    {
        using var connection = Database.Open(":memory:");
        var store = new SqliteAccountStore(connection);

        store.Set(SteamId, "oustrix", "https://example.invalid/avatar_full.jpg");

        var current = store.Current;
        Assert.NotNull(current);
        Assert.Equal(SteamId, current.SteamId64);
        Assert.Equal("oustrix", current.PersonaName);
        Assert.Equal("https://example.invalid/avatar_full.jpg", current.AvatarUrl);
    }

    [Fact]
    public void ReplacesTheAccountInsteadOfAddingARow()
    {
        using var connection = Database.Open(":memory:");
        var store = new SqliteAccountStore(connection);

        store.Set(SteamId, "oustrix", "a");
        store.Set(76561190000000003, "someone-else", "b");

        Assert.Equal(76561190000000003UL, store.Current!.SteamId64);
        Assert.Equal(1, Dapper.SqlMapper.QuerySingle<long>(connection, "SELECT COUNT(*) FROM settings"));
    }

    [Fact]
    public void PreservesTheAccentWhenWritingTheAccount()
    {
        using var connection = Database.Open(":memory:");
        new SqliteUserPreferences(connection).SetAccent("#c98f7a");

        new SqliteAccountStore(connection).Set(SteamId, "oustrix", "a");

        Assert.Equal("#c98f7a", new SqliteUserPreferences(connection).Accent);
    }

    [Fact]
    public void ReportsNoRejectionBeforeOneHappened()
    {
        using var connection = Database.Open(":memory:");

        Assert.Null(new SqliteAccountStore(connection).KeyRejectedAt);
    }

    [Fact]
    public void RoundTripsTheRejectionTimestamp()
    {
        using var connection = Database.Open(":memory:");
        var store = new SqliteAccountStore(connection);
        var at = new DateTimeOffset(2026, 7, 26, 12, 30, 0, TimeSpan.Zero);

        store.MarkKeyRejected(at);

        Assert.Equal(at, store.KeyRejectedAt);
    }

    [Fact]
    public void ClearsTheRejectionWithoutDisturbingTheAccount()
    {
        using var connection = Database.Open(":memory:");
        var store = new SqliteAccountStore(connection);
        store.Set(SteamId, "oustrix", "a");
        store.MarkKeyRejected(DateTimeOffset.UtcNow);

        store.ClearKeyRejected();

        Assert.Null(store.KeyRejectedAt);
        Assert.Equal(SteamId, store.Current!.SteamId64);
    }

    [Fact]
    public void MarksRejectionEvenWhenNoSettingsRowExistsYet()
    {
        using var connection = Database.Open(":memory:");

        new SqliteAccountStore(connection).MarkKeyRejected(DateTimeOffset.UtcNow);

        Assert.NotNull(new SqliteAccountStore(connection).KeyRejectedAt);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: FAIL — `SqliteAccountStore` could not be found.

- [ ] **Step 3: Add the migration column**

In `SteamAchievements.Core/Data/Database.cs`, at the end of `Migrate`, the file currently ends with a single `EnsureColumn` call. Replace that line:

```csharp
        EnsureColumn(connection, "settings", "accent", "TEXT");
```

with:

```csharp
        EnsureColumn(connection, "settings", "accent", "TEXT");

        // Set when Steam rejects the key, cleared when a key is accepted or a
        // sync succeeds. Persisted rather than held in memory: otherwise a
        // restart makes the application look healthy and the user spends
        // requests rediscovering what was already known.
        EnsureColumn(connection, "settings", "key_rejected_at", "TEXT");
```

- [ ] **Step 4: Write the account store**

Create `SteamAchievements.Core/Data/IAccountStore.cs`:

```csharp
namespace SteamAchievements.Core.Data;

/// <summary>The Steam account this database belongs to.</summary>
public sealed record StoredAccount(ulong SteamId64, string PersonaName, string AvatarUrl);

/// <summary>
/// Reads and writes the identity columns of <c>settings</c> and the
/// key-rejection flag. Kept apart from <c>IUserPreferences</c> so that
/// interface keeps its honest framing as the only thing the UI writes.
/// </summary>
public interface IAccountStore
{
    /// <summary>Null until onboarding has stored an account.</summary>
    StoredAccount? Current { get; }

    void Set(ulong steamId64, string personaName, string avatarUrl);

    DateTimeOffset? KeyRejectedAt { get; }

    void MarkKeyRejected(DateTimeOffset at);

    void ClearKeyRejected();
}
```

Create `SteamAchievements.Core/Data/SqliteAccountStore.cs`:

```csharp
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace SteamAchievements.Core.Data;

/// <summary>
/// Expects the settings connection — the third one, the one carrying a
/// busy timeout. WAL permits a single writer, so a settings write during a sync
/// waits rather than failing with SQLITE_BUSY.
/// </summary>
public sealed class SqliteAccountStore : IAccountStore
{
    private readonly SqliteConnection _connection;

    public SqliteAccountStore(SqliteConnection connection) => _connection = connection;

    // Dapper needs an exact CLR type match and no snake_case translation, so
    // every column is aliased and every value arrives as the type the schema
    // declares. steam_id64 is TEXT in the schema, so it is read as a string and
    // parsed here.
    private sealed record AccountRow(string? SteamId64, string? PersonaName, string? AvatarUrl);

    public StoredAccount? Current
    {
        get
        {
            var row = _connection.QuerySingleOrDefault<AccountRow>("""
                SELECT steam_id64   AS SteamId64,
                       persona_name AS PersonaName,
                       avatar_url   AS AvatarUrl
                FROM settings WHERE id = 1
                """);

            if (row?.SteamId64 is null || !ulong.TryParse(row.SteamId64, out var steamId) || steamId == 0)
            {
                return null;
            }

            return new StoredAccount(steamId, row.PersonaName ?? string.Empty, row.AvatarUrl ?? string.Empty);
        }
    }

    public void Set(ulong steamId64, string personaName, string avatarUrl) => _connection.Execute("""
        INSERT INTO settings (id, steam_id64, persona_name, avatar_url)
        VALUES (1, @SteamId, @Persona, @Avatar)
        ON CONFLICT(id) DO UPDATE SET
            steam_id64   = excluded.steam_id64,
            persona_name = excluded.persona_name,
            avatar_url   = excluded.avatar_url;
        """, new
    {
        SteamId = steamId64.ToString(CultureInfo.InvariantCulture),
        Persona = personaName,
        Avatar = avatarUrl,
    });

    public DateTimeOffset? KeyRejectedAt
    {
        get
        {
            var stored = _connection.QuerySingleOrDefault<string?>(
                "SELECT key_rejected_at FROM settings WHERE id = 1");

            return stored is null ? null : DateTimeOffset.Parse(stored, CultureInfo.InvariantCulture);
        }
    }

    public void MarkKeyRejected(DateTimeOffset at) => WriteRejection(at.ToString("o"));

    public void ClearKeyRejected() => WriteRejection(null);

    private void WriteRejection(string? value) => _connection.Execute("""
        INSERT INTO settings (id, key_rejected_at) VALUES (1, @Value)
        ON CONFLICT(id) DO UPDATE SET key_rejected_at = excluded.key_rejected_at;
        """, new { Value = value });
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: PASS. The existing `DatabaseMigrationTests` must still pass — `EnsureColumn` is idempotent and the migration test runs it twice.

- [ ] **Step 6: Commit**

```bash
git add SteamAchievements.Core/Data/Database.cs \
        SteamAchievements.Core/Data/IAccountStore.cs \
        SteamAchievements.Core/Data/SqliteAccountStore.cs \
        SteamAchievements.Core.Tests/Data/SqliteAccountStoreTests.cs
git commit -m "feat: store the Steam account and the key rejection flag"
```

---

## Task 3: Resetting the library

Both "switch account" and "reset the database" are this one operation. It cannot delete the file — three connections are open against it and Windows does not delete open files.

**Files:**
- Modify: `SteamAchievements.Core/Data/Database.cs` (add a method)
- Test: `SteamAchievements.Core.Tests/Data/DatabaseResetTests.cs`

**Interfaces:**
- Consumes: `SqliteAccountStore`, `StoredAccount` from Task 2.
- Produces: `Database.ResetLibrary(SqliteConnection connection)`, returning `void`.

- [ ] **Step 1: Write the failing test**

Create `SteamAchievements.Core.Tests/Data/DatabaseResetTests.cs`:

```csharp
using Dapper;
using Microsoft.Data.Sqlite;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Data;

public class DatabaseResetTests
{
    private static readonly ulong SteamId = 76561190000000002;

    private static SqliteConnection Populated()
    {
        var connection = Database.Open(":memory:");
        var repository = new GameRepository(connection);

        repository.UpsertOwnedGames([new OwnedGame(220, "Half-Life 2", "hash", 600, 0, null)]);
        repository.UpsertSchema(220, [new AchievementSchema("A", "First", "", "", "", false, 0)], DateTimeOffset.UtcNow);
        repository.UpsertPlayerAchievements(220, [new PlayerAchievement("A", true, DateTimeOffset.UtcNow)]);
        repository.WriteSnapshot(DateTimeOffset.UtcNow);

        new SqliteAccountStore(connection).Set(SteamId, "oustrix", "avatar");
        new SqliteUserPreferences(connection).SetAccent("#c98f7a");
        connection.Execute("""
            INSERT INTO sync_runs (started_at, kind, games_synced, duration_ms, error)
            VALUES ('2026-07-26T10:00:00.0000000+00:00', 'full', 1, 1000, NULL)
            """);

        return connection;
    }

    [Fact]
    public void RemovesEveryTraceOfTheLibrary()
    {
        using var connection = Populated();

        Database.ResetLibrary(connection);

        foreach (var table in new[]
                 {
                     "games", "owned_games", "achievements", "global_percents",
                     "player_achievements", "sync_state", "snapshots", "sync_runs",
                 })
        {
            Assert.Equal(0, connection.QuerySingle<long>($"SELECT COUNT(*) FROM {table}"));
        }
    }

    [Fact]
    public void ForgetsTheAccount()
    {
        using var connection = Populated();

        Database.ResetLibrary(connection);

        Assert.Null(new SqliteAccountStore(connection).Current);
    }

    [Fact]
    public void KeepsTheAccentBecauseItIsTasteRatherThanAccountData()
    {
        using var connection = Populated();

        Database.ResetLibrary(connection);

        Assert.Equal("#c98f7a", new SqliteUserPreferences(connection).Accent);
    }

    [Fact]
    public void ClearsTheKeyRejectionFlag()
    {
        using var connection = Populated();
        new SqliteAccountStore(connection).MarkKeyRejected(DateTimeOffset.UtcNow);

        Database.ResetLibrary(connection);

        Assert.Null(new SqliteAccountStore(connection).KeyRejectedAt);
    }

    [Fact]
    public void LeavesTheSchemaIntactSoTheApplicationKeepsWorking()
    {
        using var connection = Populated();

        Database.ResetLibrary(connection);
        new GameRepository(connection).UpsertOwnedGames(
            [new OwnedGame(440, "Team Fortress 2", "hash", 10, 0, null)]);

        Assert.Equal(1, connection.QuerySingle<long>("SELECT COUNT(*) FROM owned_games"));
    }

    [Fact]
    public void IsSafeToRunTwice()
    {
        using var connection = Populated();

        Database.ResetLibrary(connection);
        Database.ResetLibrary(connection);

        Assert.Equal(0, connection.QuerySingle<long>("SELECT COUNT(*) FROM games"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: FAIL — `Database` does not contain a definition for `ResetLibrary`.

- [ ] **Step 3: Write the implementation**

Add to `SteamAchievements.Core/Data/Database.cs`, after `Migrate` and before `EnsureColumn`:

```csharp
    /// <summary>
    /// Empties the library so a different Steam account can be synced into it,
    /// or so a broken cache can be rebuilt. These are the same operation: no
    /// table carries a SteamID column, so the database implicitly belongs to one
    /// account and mixing two produces silently wrong data with nothing in the
    /// schema to tell them apart afterwards.
    ///
    /// Deletes rows rather than the file. Three connections are open against it
    /// and Windows does not delete open files; tearing the whole connection graph
    /// down and rebuilding it would be far more code than this.
    ///
    /// Keeps <c>settings.accent</c>. That is the user's taste rather than the
    /// account's data, and losing it on "switch account" is a surprise beyond
    /// what the confirmation promised.
    /// </summary>
    public static void ResetLibrary(SqliteConnection connection)
    {
        using (var transaction = connection.BeginTransaction())
        {
            connection.Execute("""
                DELETE FROM player_achievements;
                DELETE FROM global_percents;
                DELETE FROM achievements;
                DELETE FROM sync_state;
                DELETE FROM owned_games;
                DELETE FROM games;
                DELETE FROM snapshots;
                DELETE FROM sync_runs;

                UPDATE settings
                   SET steam_id64        = NULL,
                       persona_name      = NULL,
                       avatar_url        = NULL,
                       last_full_sync_at = NULL,
                       key_rejected_at   = NULL
                 WHERE id = 1;
                """, transaction: transaction);

            transaction.Commit();
        }

        // VACUUM cannot run inside a transaction, so it is deliberately outside
        // the block above. Without it the file keeps the space the deleted rows
        // occupied, which for a 1500-game library is most of it.
        connection.Execute("VACUUM");
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SteamAchievements.Core/Data/Database.cs SteamAchievements.Core.Tests/Data/DatabaseResetTests.cs
git commit -m "feat: reset the library without deleting the database file"
```

---

## Task 4: The sync journal and a three-state run outcome

`sync_runs` and `settings.last_full_sync_at` are read by `SqliteLibraryQuery` and written by nobody. This task writes them — and fixes the fact that the history screen would report every pause as a failure.

**Files:**
- Create: `SteamAchievements.Core/Data/SyncJournal.cs`
- Modify: `SteamAchievements.Core/Presentation/SyncRunView.cs`
- Modify: `SteamAchievements.Core/Data/SqliteLibraryQuery.cs` (`GetSyncHistory` projection and `Describe`)
- Test: `SteamAchievements.Core.Tests/Data/SyncJournalTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `SyncRunRecord(DateTimeOffset StartedAt, string Kind, int GamesSynced, long DurationMs, string? Error)`. `SyncJournal` with `void RecordRun(SyncRunRecord run)`, `void MarkSyncCompleted(DateTimeOffset at)`, `DateTimeOffset? LastSyncedAt { get; }`, and the constant `SyncJournal.Cancelled` equal to `"cancelled"`. `SyncRunOutcome { Completed, Cancelled, Failed }`. `SyncRunView(string WhenText, string WhatText, string DurationText, SyncRunOutcome Outcome)`.

- [ ] **Step 1: Write the failing test**

Create `SteamAchievements.Core.Tests/Data/SyncJournalTests.cs`:

```csharp
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests.Data;

public class SyncJournalTests
{
    private static readonly DateTimeOffset Started = new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReportsNoPreviousSyncOnAFreshDatabase()
    {
        using var connection = Database.Open(":memory:");

        Assert.Null(new SyncJournal(connection).LastSyncedAt);
    }

    [Fact]
    public void RoundTripsTheCompletionTimestamp()
    {
        using var connection = Database.Open(":memory:");
        var journal = new SyncJournal(connection);

        journal.MarkSyncCompleted(Started);

        Assert.Equal(Started, journal.LastSyncedAt);
    }

    [Fact]
    public void RecordsASuccessfulRunAsCompleted()
    {
        using var connection = Database.Open(":memory:");
        new SyncJournal(connection).RecordRun(new SyncRunRecord(Started, "full", 1482, 9000, null));

        var history = new SqliteLibraryQuery(connection).GetSyncHistory(10, Now);

        Assert.Equal(SyncRunOutcome.Completed, history.Single().Outcome);
        // Composed rather than written out: Formatting.Number separates
        // thousands with a thin space (U+2009), and a literal ASCII space here
        // would fail the comparison for a reason invisible in the diff. The
        // existing SqliteLibraryQueryTests does the same.
        Assert.Equal($"Full sync — {Formatting.Number(1482)} games", history.Single().WhatText);
    }

    [Fact]
    public void RecordsACancelledRunAsCancelledRatherThanFailed()
    {
        using var connection = Database.Open(":memory:");
        new SyncJournal(connection).RecordRun(
            new SyncRunRecord(Started, "incremental", 412, 3000, SyncJournal.Cancelled));

        var run = new SqliteLibraryQuery(connection).GetSyncHistory(10, Now).Single();

        Assert.Equal(SyncRunOutcome.Cancelled, run.Outcome);
        Assert.Equal("Cancelled — 412 games", run.WhatText);
    }

    [Fact]
    public void RecordsAFailedRunWithItsMessage()
    {
        using var connection = Database.Open(":memory:");
        new SyncJournal(connection).RecordRun(
            new SyncRunRecord(Started, "incremental", 3, 400, "Steam rejected the API key."));

        var run = new SqliteLibraryQuery(connection).GetSyncHistory(10, Now).Single();

        Assert.Equal(SyncRunOutcome.Failed, run.Outcome);
        Assert.Equal("Failed — Steam rejected the API key.", run.WhatText);
    }

    [Fact]
    public void KeepsEveryRunSoAPausedSyncLeavesATrail()
    {
        using var connection = Database.Open(":memory:");
        var journal = new SyncJournal(connection);

        journal.RecordRun(new SyncRunRecord(Started, "incremental", 100, 1000, SyncJournal.Cancelled));
        journal.RecordRun(new SyncRunRecord(Started.AddMinutes(5), "incremental", 300, 1000, SyncJournal.Cancelled));
        journal.RecordRun(new SyncRunRecord(Started.AddMinutes(9), "incremental", 482, 1000, null));

        Assert.Equal(3, new SqliteLibraryQuery(connection).GetSyncHistory(10, Now).Count);
    }

    [Fact]
    public void OverwritesRatherThanThrowingWhenTwoRunsShareAStartTimestamp()
    {
        using var connection = Database.Open(":memory:");
        var journal = new SyncJournal(connection);

        journal.RecordRun(new SyncRunRecord(Started, "full", 1, 10, null));
        journal.RecordRun(new SyncRunRecord(Started, "full", 2, 20, null));

        var run = new SqliteLibraryQuery(connection).GetSyncHistory(10, Now).Single();
        Assert.Equal("Full sync — 2 games", run.WhatText);
    }
}
```

Add `using SteamAchievements.Core.Presentation;` for `Formatting` — it lives in that namespace alongside the view records.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: FAIL — `SyncJournal` could not be found.

- [ ] **Step 3: Change the run view to a three-state outcome**

Replace the whole of `SteamAchievements.Core/Presentation/SyncRunView.cs`:

```csharp
namespace SteamAchievements.Core.Presentation;

/// <summary>
/// How a recorded sync ended. A cancelled run is not a failure: pausing is
/// implemented as cancel-and-resume, so treating any non-null error as a
/// failure would report every pause as one.
/// </summary>
public enum SyncRunOutcome
{
    Completed,
    Cancelled,
    Failed,
}

public sealed record SyncRunView(
    string WhenText,
    string WhatText,
    string DurationText,
    SyncRunOutcome Outcome);
```

- [ ] **Step 4: Teach the query about cancelled runs**

In `SteamAchievements.Core/Data/SqliteLibraryQuery.cs`, in `GetSyncHistory`, replace the projection's last argument:

```csharp
                Formatting.Duration(r.DurationMs),
                r.Error is not null))
```

with:

```csharp
                Formatting.Duration(r.DurationMs),
                Outcome(r.Error)))
```

Then replace the `Describe` method with:

```csharp
    private static SyncRunOutcome Outcome(string? error) => error switch
    {
        null => SyncRunOutcome.Completed,
        SyncJournal.Cancelled => SyncRunOutcome.Cancelled,
        _ => SyncRunOutcome.Failed,
    };

    private static string Describe(SyncRunRow run)
    {
        if (run.Error == SyncJournal.Cancelled)
        {
            return $"Cancelled — {Formatting.Number(run.GamesSynced)} games";
        }

        if (run.Error is not null)
        {
            return $"Failed — {run.Error}";
        }

        var count = Formatting.Number(run.GamesSynced);

        return run.Kind switch
        {
            "full" => $"Full sync — {count} games",
            "incremental" => $"Incremental — {count} games changed",
            "schema" => $"Schema refresh — {count} games stale",
            _ => $"{run.Kind} — {count} games",
        };
    }
```

- [ ] **Step 5: Write the journal**

Create `SteamAchievements.Core/Data/SyncJournal.cs`:

```csharp
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace SteamAchievements.Core.Data;

/// <summary>One completed attempt at syncing, however it ended.</summary>
public sealed record SyncRunRecord(
    DateTimeOffset StartedAt,
    string Kind,
    int GamesSynced,
    long DurationMs,
    string? Error);

/// <summary>
/// Writes the two things the history screen and the sidebar read and nobody
/// used to write.
///
/// Expects the settings connection rather than the sync engine's. Every write
/// here happens after <c>RunAsync</c> has returned, so no sync transaction is in
/// flight; the busy timeout on that connection covers the rest.
/// </summary>
public sealed class SyncJournal
{
    /// <summary>
    /// Stored in <c>sync_runs.error</c> for a run the user stopped. Pausing is
    /// cancel-and-resume, so a paused sync leaves one of these behind and the
    /// history says so.
    /// </summary>
    public const string Cancelled = "cancelled";

    private readonly SqliteConnection _connection;

    public SyncJournal(SqliteConnection connection) => _connection = connection;

    public void RecordRun(SyncRunRecord run) => _connection.Execute("""
        INSERT INTO sync_runs (started_at, kind, games_synced, duration_ms, error)
        VALUES (@StartedAt, @Kind, @GamesSynced, @DurationMs, @Error)
        ON CONFLICT(started_at) DO UPDATE SET
            kind         = excluded.kind,
            games_synced = excluded.games_synced,
            duration_ms  = excluded.duration_ms,
            error        = excluded.error;
        """, new
    {
        StartedAt = run.StartedAt.ToString("o"),
        run.Kind,
        run.GamesSynced,
        run.DurationMs,
        run.Error,
    });

    /// <summary>
    /// The column is named <c>last_full_sync_at</c> from the first migration but
    /// means "last successful sync": the sidebar renders it as "Last sync 14 min
    /// ago" regardless of whether the run was full or incremental.
    /// </summary>
    public void MarkSyncCompleted(DateTimeOffset at) => _connection.Execute("""
        INSERT INTO settings (id, last_full_sync_at) VALUES (1, @At)
        ON CONFLICT(id) DO UPDATE SET last_full_sync_at = excluded.last_full_sync_at;
        """, new { At = at.ToString("o") });

    public DateTimeOffset? LastSyncedAt
    {
        get
        {
            var stored = _connection.QuerySingleOrDefault<string?>(
                "SELECT last_full_sync_at FROM settings WHERE id = 1");

            return stored is null ? null : DateTimeOffset.Parse(stored, CultureInfo.InvariantCulture);
        }
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: PASS. If `SqliteLibraryQueryTests` fails to compile, it is asserting on the removed `Failed` property — update it to `Outcome`.

- [ ] **Step 7: Commit**

```bash
git add SteamAchievements.Core/Data/SyncJournal.cs \
        SteamAchievements.Core/Data/SqliteLibraryQuery.cs \
        SteamAchievements.Core/Presentation/SyncRunView.cs \
        SteamAchievements.Core.Tests/Data
git commit -m "feat: record sync runs and distinguish cancelled from failed"
```

---

## Task 5: Onboarding state and locating Steam accounts

**Files:**
- Create: `SteamAchievements.Core/App/OnboardingState.cs`
- Create: `SteamAchievements.Core/App/SteamAccountLocator.cs`
- Test: `SteamAchievements.Core.Tests/App/OnboardingStateTests.cs`
- Test: `SteamAchievements.Core.Tests/App/SteamAccountLocatorTests.cs`

**Interfaces:**
- Consumes: `ISteamPathProvider` (already in `Core/Abstractions`), `LoginUsersReader` and `SteamAccount` (already in `Core/Local`).
- Produces: `OnboardingStep { ChooseAccount, EnterKey, Ready }`, `OnboardingState.Evaluate(ulong? storedSteamId, bool hasKey)`. `SteamAccountLocator(ISteamPathProvider paths)` with `IReadOnlyList<SteamAccount> FindAccounts()` and `SteamAccount? FindActiveAccount()`.

- [ ] **Step 1: Write the failing tests**

Create `SteamAchievements.Core.Tests/App/OnboardingStateTests.cs`:

```csharp
using SteamAchievements.Core.App;

namespace SteamAchievements.Core.Tests.App;

public class OnboardingStateTests
{
    private static readonly ulong SteamId = 76561190000000002;

    [Fact]
    public void StartsAtAccountSelectionOnAFreshInstall()
    {
        Assert.Equal(OnboardingStep.ChooseAccount, OnboardingState.Evaluate(null, hasKey: false));
    }

    [Fact]
    public void StillAsksForAnAccountWhenAKeyExistsButAnAccountDoesNot()
    {
        Assert.Equal(OnboardingStep.ChooseAccount, OnboardingState.Evaluate(null, hasKey: true));
    }

    [Fact]
    public void TreatsAZeroSteamIdAsNoAccount()
    {
        Assert.Equal(OnboardingStep.ChooseAccount, OnboardingState.Evaluate(0, hasKey: true));
    }

    [Fact]
    public void AsksForAKeyOnceAnAccountIsKnown()
    {
        Assert.Equal(OnboardingStep.EnterKey, OnboardingState.Evaluate(SteamId, hasKey: false));
    }

    [Fact]
    public void IsReadyWithBoth()
    {
        Assert.Equal(OnboardingStep.Ready, OnboardingState.Evaluate(SteamId, hasKey: true));
    }
}
```

Create `SteamAchievements.Core.Tests/App/SteamAccountLocatorTests.cs`:

```csharp
using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.App;

namespace SteamAchievements.Core.Tests.App;

public class SteamAccountLocatorTests
{
    private sealed class FixedPath(string? path) : ISteamPathProvider
    {
        public string? FindSteamPath() => path;
    }

    /// <summary>
    /// The locator expects a Steam root and looks for config/loginusers.vdf
    /// underneath it, so the committed fixture is copied into that shape.
    /// </summary>
    private static string SteamRootWithFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.Copy(TestPaths.Data("loginusers.vdf"), Path.Combine(root, "config", "loginusers.vdf"));
        return root;
    }

    [Fact]
    public void FindsNothingWhenSteamIsNotInstalled()
    {
        Assert.Empty(new SteamAccountLocator(new FixedPath(null)).FindAccounts());
        Assert.Null(new SteamAccountLocator(new FixedPath(null)).FindActiveAccount());
    }

    [Fact]
    public void FindsNothingWhenTheSteamFolderHasNoLoginFile()
    {
        var empty = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(empty);
        try
        {
            Assert.Empty(new SteamAccountLocator(new FixedPath(empty)).FindAccounts());
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public void ReadsTheAccountsOutOfTheLoginFile()
    {
        var root = SteamRootWithFixture();
        try
        {
            Assert.NotEmpty(new SteamAccountLocator(new FixedPath(root)).FindAccounts());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PicksTheAccountSteamMarkedMostRecent()
    {
        var root = SteamRootWithFixture();
        try
        {
            // The committed fixture holds two accounts: 76561190000000001
            // ("olduser", MostRecent 0) and 76561190000000002 ("currentuser",
            // MostRecent 1).
            var active = new SteamAccountLocator(new FixedPath(root)).FindActiveAccount();

            Assert.NotNull(active);
            Assert.Equal(76561190000000002UL, active.SteamId64);
            Assert.Equal("currentuser", active.AccountName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SurvivesAMalformedLoginFileInsteadOfThrowing()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "config", "loginusers.vdf"), "\"users\"\n{\n  \"7656119\"");
        try
        {
            Assert.Empty(new SteamAccountLocator(new FixedPath(root)).FindAccounts());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: FAIL — `OnboardingState` and `SteamAccountLocator` could not be found.

- [ ] **Step 3: Write the implementation**

Create `SteamAchievements.Core/App/OnboardingState.cs`:

```csharp
namespace SteamAchievements.Core.App;

public enum OnboardingStep
{
    ChooseAccount,
    EnterKey,
    Ready,
}

/// <summary>
/// The whole of "is onboarding complete", as two inputs and one output. The
/// shell reads this to decide whether to draw its chrome, and the host reads it
/// to decide the WebView's start path.
/// </summary>
public static class OnboardingState
{
    public static OnboardingStep Evaluate(ulong? storedSteamId, bool hasKey)
    {
        if (storedSteamId is null or 0)
        {
            return OnboardingStep.ChooseAccount;
        }

        return hasKey ? OnboardingStep.Ready : OnboardingStep.EnterKey;
    }
}
```

Create `SteamAchievements.Core/App/SteamAccountLocator.cs`:

```csharp
using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.Local;

namespace SteamAchievements.Core.App;

/// <summary>
/// Turns "where is Steam" into "who is signed in". Reading a file is not a
/// Windows API, so all of this stays testable on macOS with a fake path
/// provider; only <see cref="ISteamPathProvider"/> itself needs the registry.
/// </summary>
public sealed class SteamAccountLocator
{
    private readonly ISteamPathProvider _paths;

    public SteamAccountLocator(ISteamPathProvider paths) => _paths = paths;

    public static string LoginUsersPath(string steamPath) =>
        Path.Combine(steamPath, "config", "loginusers.vdf");

    /// <summary>
    /// Empty whenever the answer cannot be had — Steam is not installed, the
    /// file is missing, unreadable, or malformed. None of those is exceptional:
    /// they all lead to the same screen, where the user types a SteamID by hand.
    /// </summary>
    public IReadOnlyList<SteamAccount> FindAccounts()
    {
        var steamPath = _paths.FindSteamPath();

        if (string.IsNullOrWhiteSpace(steamPath))
        {
            return [];
        }

        var file = LoginUsersPath(steamPath);

        if (!File.Exists(file))
        {
            return [];
        }

        try
        {
            return LoginUsersReader.Read(File.ReadAllText(file));
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            // VdfParser throws FormatException on unbalanced braces and
            // unterminated strings — a half-written file during a Steam update
            // looks exactly like that.
            return [];
        }
    }

    public SteamAccount? FindActiveAccount() => LoginUsersReader.SelectActive(FindAccounts());
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SteamAchievements.Core/App/OnboardingState.cs \
        SteamAchievements.Core/App/SteamAccountLocator.cs \
        SteamAchievements.Core.Tests/App
git commit -m "feat: evaluate onboarding state and locate signed-in Steam accounts"
```

---

## Task 6: The public profile endpoint

The only Steam endpoint that answers without a key and gives a name and an avatar. Used once, on the "is this you?" step.

**Files:**
- Create: `testdata/profile_public.xml`
- Create: `testdata/profile_not_found.xml`
- Create: `SteamAchievements.Core/Steam/SteamCommunityClient.cs`
- Modify: `docs/steam-api.md`
- Test: `SteamAchievements.Core.Tests/Steam/SteamCommunityClientTests.cs`

**Interfaces:**
- Consumes: `FakeHttpMessageHandler` from `SteamAchievements.Core.Tests/Steam/`.
- Produces: `PublicProfile(ulong SteamId64, string PersonaName, string AvatarUrl)`. `SteamCommunityClient(HttpClient http)` with `Task<PublicProfile?> GetProfileAsync(ulong steamId64, CancellationToken cancellationToken)`.

- [ ] **Step 1: Add the fixtures**

Create `testdata/profile_public.xml`. This is a real response with the SteamID64, persona name and avatar hash replaced:

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?><profile>
	<steamID64>76561190000000002</steamID64>
	<steamID><![CDATA[oustrix]]></steamID>
	<onlineState>offline</onlineState>
	<stateMessage><![CDATA[Offline]]></stateMessage>
	<privacyState>friendsonly</privacyState>
	<visibilityState>1</visibilityState>
	<avatarIcon><![CDATA[https://avatars.akamai.steamstatic.com/0000000000000000000000000000000000000000.jpg]]></avatarIcon>
	<avatarMedium><![CDATA[https://avatars.akamai.steamstatic.com/0000000000000000000000000000000000000000_medium.jpg]]></avatarMedium>
	<avatarFull><![CDATA[https://avatars.akamai.steamstatic.com/0000000000000000000000000000000000000000_full.jpg]]></avatarFull>
	<vacBanned>0</vacBanned>
	<tradeBanState>None</tradeBanState>
	<isLimitedAccount>0</isLimitedAccount>
</profile>
```

Create `testdata/profile_not_found.xml`, exactly as Steam returns it — note that this arrives with **HTTP 200**:

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?><response><error><![CDATA[The specified profile could not be found.]]></error></response>
```

- [ ] **Step 2: Write the failing test**

Create `SteamAchievements.Core.Tests/Steam/SteamCommunityClientTests.cs`:

```csharp
using System.Net;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Steam;

public class SteamCommunityClientTests
{
    private static readonly ulong SteamId = 76561190000000002;

    private static SteamCommunityClient Client(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://steamcommunity.com/") });

    [Fact]
    public async Task ReadsTheNameAndAvatarOutOfTheProfileDocument()
    {
        var xml = await File.ReadAllTextAsync(TestPaths.Data("profile_public.xml"));
        var client = Client(FakeHttpMessageHandler.Returning(HttpStatusCode.OK, xml, "text/xml"));

        var profile = await client.GetProfileAsync(SteamId, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal(SteamId, profile.SteamId64);
        Assert.Equal("oustrix", profile.PersonaName);
        Assert.EndsWith("_full.jpg", profile.AvatarUrl);
    }

    [Fact]
    public async Task AsksTheCommunitySiteForTheXmlVariantOfTheProfile()
    {
        var xml = await File.ReadAllTextAsync(TestPaths.Data("profile_public.xml"));
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, xml, "text/xml");

        await Client(handler).GetProfileAsync(SteamId, CancellationToken.None);

        var requested = handler.Requests.Single().ToString();
        Assert.Contains($"/profiles/{SteamId}/", requested);
        Assert.Contains("xml=1", requested);
    }

    [Fact]
    public async Task ReturnsNullForAProfileThatDoesNotExistEvenThoughSteamAnswersTwoHundred()
    {
        var xml = await File.ReadAllTextAsync(TestPaths.Data("profile_not_found.xml"));
        var client = Client(FakeHttpMessageHandler.Returning(HttpStatusCode.OK, xml, "text/xml"));

        Assert.Null(await client.GetProfileAsync(SteamId, CancellationToken.None));
    }

    [Fact]
    public async Task ReturnsNullWhenTheCommunitySiteServesHtml()
    {
        var client = Client(FakeHttpMessageHandler.Returning(
            HttpStatusCode.OK, "<html><body>Sign In</body></html>", "text/html"));

        Assert.Null(await client.GetProfileAsync(SteamId, CancellationToken.None));
    }

    [Fact]
    public async Task ReturnsNullOnAnErrorStatus()
    {
        var client = Client(FakeHttpMessageHandler.Returning(
            HttpStatusCode.ServiceUnavailable, string.Empty, "text/html"));

        Assert.Null(await client.GetProfileAsync(SteamId, CancellationToken.None));
    }

    [Fact]
    public async Task ReturnsNullWhenTheNetworkIsDown()
    {
        var client = Client(new FakeHttpMessageHandler(_ => throw new HttpRequestException("no route")));

        Assert.Null(await client.GetProfileAsync(SteamId, CancellationToken.None));
    }

    [Fact]
    public async Task StillHonoursCancellation()
    {
        var xml = await File.ReadAllTextAsync(TestPaths.Data("profile_public.xml"));
        var client = Client(FakeHttpMessageHandler.Returning(HttpStatusCode.OK, xml, "text/xml"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetProfileAsync(SteamId, cancelled.Token));
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: FAIL — `SteamCommunityClient` could not be found.

- [ ] **Step 4: Write the implementation**

Create `SteamAchievements.Core/Steam/SteamCommunityClient.cs`:

```csharp
using System.Xml.Linq;

namespace SteamAchievements.Core.Steam;

public sealed record PublicProfile(ulong SteamId64, string PersonaName, string AvatarUrl);

/// <summary>
/// The community site's public profile document. A separate class from
/// <see cref="SteamApiClient"/> on purpose: a different host, XML instead of
/// JSON, different error semantics and no API key. It does not belong next to
/// that client's <c>GetJsonAsync</c>.
///
/// Expects an <see cref="HttpClient"/> whose base address is
/// <c>https://steamcommunity.com/</c>.
/// </summary>
public sealed class SteamCommunityClient
{
    private readonly HttpClient _http;

    public SteamCommunityClient(HttpClient http) => _http = http;

    /// <summary>
    /// Null whenever the profile cannot be read, for any reason. This is used
    /// only to put a name and a picture on the "is this you?" step, so it must
    /// never block onboarding and never throws for a failure of its own — only
    /// for caller cancellation.
    /// </summary>
    public async Task<PublicProfile?> GetProfileAsync(ulong steamId64, CancellationToken cancellationToken)
    {
        string body;

        try
        {
            using var response = await _http.GetAsync($"profiles/{steamId64}/?xml=1", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return null;
        }

        XDocument document;

        try
        {
            document = XDocument.Parse(body);
        }
        catch (System.Xml.XmlException)
        {
            // An anonymous request for a private area is answered with the
            // login page, which HttpClient follows the redirect to and hands
            // back as HTML.
            return null;
        }

        // Verified 2026-07-26: a profile that does not exist answers HTTP 200
        // with <response><error>...</error></response>. The status code carries
        // no information, so the root element is the only reliable signal.
        var profile = document.Root;

        if (profile is null || profile.Name.LocalName != "profile")
        {
            return null;
        }

        return new PublicProfile(
            steamId64,
            (string?)profile.Element("steamID") ?? string.Empty,
            (string?)profile.Element("avatarFull") ?? string.Empty);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: PASS.

- [ ] **Step 6: Record what was verified in the API reference**

In `docs/steam-api.md`, in the "What is closed off" section, replace the line:

```markdown
- Only `/profiles/<id>/?xml=1` remains public: persona name, avatar, online
  state. Used during onboarding for the "is this you?" step.
```

with:

```markdown
- Only `/profiles/<id>/?xml=1` remains public: persona name, avatar, online
  state. Used during onboarding for the "is this you?" step. Re-verified
  2026-07-26.

  The document's root element is `<profile>`, and every text field is wrapped
  in `CDATA`. The two fields onboarding uses are `<steamID>` (the persona name,
  *not* the id) and `<avatarFull>`.

  A `privacyState` of `friendsonly` still returns the name and the avatar, so
  privacy does not have to be treated as a failure.

  **A profile that does not exist answers HTTP 200**, with
  `<response><error>The specified profile could not be found.</error></response>`.
  The status code carries no information here; a parser has to branch on the
  root element.
```

- [ ] **Step 7: Commit**

```bash
git add testdata/profile_public.xml testdata/profile_not_found.xml \
        SteamAchievements.Core/Steam/SteamCommunityClient.cs \
        SteamAchievements.Core.Tests/Steam/SteamCommunityClientTests.cs \
        docs/steam-api.md
git commit -m "feat: read persona name and avatar from the public profile endpoint"
```

---

## Task 7: The sync state machine

The largest task, and the one that pays for itself: after it, "what happens when Steam revokes the key on the four-hundredth game" is answered by `dotnet test` instead of by a Windows machine.

**Files:**
- Create: `SteamAchievements.Core/Presentation/SyncStatusView.cs`
- Create: `SteamAchievements.Core/Sync/ISyncRunner.cs`
- Create: `SteamAchievements.Core/Sync/LiveSyncRunner.cs`
- Create: `SteamAchievements.Core/App/SyncCoordinator.cs`
- Test: `SteamAchievements.Core.Tests/App/SyncCoordinatorTests.cs`

**Interfaces:**
- Consumes: `IAccountStore`, `StoredAccount` (Task 2), `SyncJournal`, `SyncRunRecord` (Task 4), `ISecretStore` (Task 1), `SyncOrchestrator`, `SyncOptions`, `SyncProgress`, `SteamApiClient`, `SteamApiException`, `SteamApiErrorKind` (all pre-existing).
- Produces: `SyncPhase { NeverRun, Idle, Running, Paused, Failed, KeyRejected }`. `SyncStatusView(SyncPhase Phase, int Completed, int Total, string CurrentGame, string Headline, string? Detail, string? Error)`. `ISyncPresenter { SyncStatusView Status; event Action? Changed; }`. `ISyncController { void Start(bool force); void Pause(); void Cancel(); }`. `ISyncRunner.RunAsync(ulong, bool, IProgress<SyncProgress>?, CancellationToken)`. `LiveSyncRunner(ISecretStore, GameRepository, Func<string, SteamApiClient>)`. `SyncCoordinator(ISyncRunner, IAccountStore, SyncJournal, Func<DateTimeOffset>)` implementing both interfaces plus `IDisposable`, with `Task Completion { get; }`.

- [ ] **Step 1: Write the failing test**

Create `SteamAchievements.Core.Tests/App/SyncCoordinatorTests.cs`:

```csharp
using SteamAchievements.Core.App;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Sync;

namespace SteamAchievements.Core.Tests.App;

public class SyncCoordinatorTests
{
    private static readonly ulong SteamId = 76561190000000002;
    private static readonly DateTimeOffset Start = new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Every run reads the clock exactly twice — once when it starts, once when
    /// it ends — so a clock that advances one second per read makes every
    /// recorded duration exactly 1000 ms.
    /// </summary>
    private sealed class SteppingClock
    {
        private DateTimeOffset _now = Start;

        public DateTimeOffset Read()
        {
            var current = _now;
            _now = _now.AddSeconds(1);
            return current;
        }
    }

    private sealed class FakeSyncRunner : ISyncRunner
    {
        private readonly Func<IProgress<SyncProgress>?, CancellationToken, Task> _behaviour;

        public FakeSyncRunner(Func<IProgress<SyncProgress>?, CancellationToken, Task> behaviour) =>
            _behaviour = behaviour;

        public bool LastForce { get; private set; }

        public ulong LastSteamId { get; private set; }

        public Task RunAsync(
            ulong steamId, bool force, IProgress<SyncProgress>? progress, CancellationToken cancellationToken)
        {
            LastSteamId = steamId;
            LastForce = force;
            return _behaviour(progress, cancellationToken);
        }
    }

    /// <summary>A task that only ends when the caller cancels.</summary>
    private static Task UntilCancelled(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource();
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return completion.Task;
    }

    private static (SyncCoordinator Coordinator, Microsoft.Data.Sqlite.SqliteConnection Connection, IAccountStore Accounts)
        Build(ISyncRunner runner, bool withAccount = true)
    {
        var connection = Database.Open(":memory:");
        var accounts = new SqliteAccountStore(connection);

        if (withAccount)
        {
            accounts.Set(SteamId, "oustrix", "avatar");
        }

        var clock = new SteppingClock();
        return (new SyncCoordinator(runner, accounts, new SyncJournal(connection), clock.Read), connection, accounts);
    }

    [Fact]
    public void ReportsNeverRunBeforeTheFirstSync()
    {
        var (coordinator, connection, _) = Build(new FakeSyncRunner((_, _) => Task.CompletedTask));
        using (connection)
        using (coordinator)
        {
            Assert.Equal(SyncPhase.NeverRun, coordinator.Status.Phase);
        }
    }

    [Fact]
    public void ReportsIdleWhenAPreviousSyncAlreadySucceeded()
    {
        var connection = Database.Open(":memory:");
        var accounts = new SqliteAccountStore(connection);
        accounts.Set(SteamId, "oustrix", "avatar");
        var journal = new SyncJournal(connection);
        journal.MarkSyncCompleted(Start);

        var clock = new SteppingClock();
        using (connection)
        using (var coordinator = new SyncCoordinator(
                   new FakeSyncRunner((_, _) => Task.CompletedTask), accounts, journal, clock.Read))
        {
            Assert.Equal(SyncPhase.Idle, coordinator.Status.Phase);
        }
    }

    [Fact]
    public void ReportsKeyRejectedAtStartupWhenTheFlagIsSet()
    {
        var (coordinator, connection, accounts) = Build(new FakeSyncRunner((_, _) => Task.CompletedTask));
        using (connection)
        {
            coordinator.Dispose();
            accounts.MarkKeyRejected(Start);

            var clock = new SteppingClock();
            using var fresh = new SyncCoordinator(
                new FakeSyncRunner((_, _) => Task.CompletedTask), accounts, new SyncJournal(connection), clock.Read);

            Assert.Equal(SyncPhase.KeyRejected, fresh.Status.Phase);
        }
    }

    [Fact]
    public async Task PublishesProgressWhileRunningAndSettlesOnIdle()
    {
        var seen = new List<SyncStatusView>();
        var runner = new FakeSyncRunner((progress, _) =>
        {
            progress!.Report(new SyncProgress(1, 2, "Half-Life 2"));
            progress.Report(new SyncProgress(2, 2, "Portal"));
            return Task.CompletedTask;
        });

        var (coordinator, connection, _) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Changed += () => seen.Add(coordinator.Status);
            coordinator.Start(force: false);
            await coordinator.Completion;

            Assert.Contains(seen, s => s is { Phase: SyncPhase.Running, Completed: 1, CurrentGame: "Half-Life 2" });
            Assert.Equal(SyncPhase.Idle, coordinator.Status.Phase);
        }
    }

    [Fact]
    public async Task RecordsASuccessfulRunAndItsCompletionTime()
    {
        var runner = new FakeSyncRunner((progress, _) =>
        {
            progress!.Report(new SyncProgress(7, 7, "Portal"));
            return Task.CompletedTask;
        });

        var (coordinator, connection, _) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: true);
            await coordinator.Completion;

            var run = new SqliteLibraryQuery(connection).GetSyncHistory(10, Start.AddHours(1)).Single();
            Assert.Equal(SyncRunOutcome.Completed, run.Outcome);
            Assert.Contains("Full sync", run.WhatText);
            Assert.Equal(Start.AddSeconds(1), new SyncJournal(connection).LastSyncedAt);
        }
    }

    [Fact]
    public async Task PassesForceThroughToTheRunner()
    {
        var runner = new FakeSyncRunner((_, _) => Task.CompletedTask);
        var (coordinator, connection, _) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: true);
            await coordinator.Completion;

            Assert.True(runner.LastForce);
            Assert.Equal(SteamId, runner.LastSteamId);
        }
    }

    [Fact]
    public async Task PausingLeavesTheProgressVisibleAndRecordsACancelledRun()
    {
        var runner = new FakeSyncRunner((progress, token) =>
        {
            progress!.Report(new SyncProgress(412, 1482, "Stellaris"));
            return UntilCancelled(token);
        });

        var (coordinator, connection, _) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            coordinator.Pause();
            await coordinator.Completion;

            Assert.Equal(SyncPhase.Paused, coordinator.Status.Phase);
            Assert.Equal(412, coordinator.Status.Completed);
            Assert.Equal(1482, coordinator.Status.Total);

            var run = new SqliteLibraryQuery(connection).GetSyncHistory(10, Start.AddHours(1)).Single();
            Assert.Equal(SyncRunOutcome.Cancelled, run.Outcome);
        }
    }

    [Fact]
    public async Task CancellingDiffersFromPausingOnlyInThePhaseItLeavesBehind()
    {
        var runner = new FakeSyncRunner((progress, token) =>
        {
            progress!.Report(new SyncProgress(5, 100, "Stellaris"));
            return UntilCancelled(token);
        });

        var (coordinator, connection, _) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            coordinator.Cancel();
            await coordinator.Completion;

            Assert.Equal(SyncPhase.Idle, coordinator.Status.Phase);
            Assert.Equal(
                SyncRunOutcome.Cancelled,
                new SqliteLibraryQuery(connection).GetSyncHistory(10, Start.AddHours(1)).Single().Outcome);
        }
    }

    [Fact]
    public async Task ResumingAfterAPauseIsJustStartingAgain()
    {
        var runs = 0;
        var runner = new FakeSyncRunner((progress, token) =>
        {
            runs++;
            progress!.Report(new SyncProgress(10, 100, "Stellaris"));
            return runs == 1 ? UntilCancelled(token) : Task.CompletedTask;
        });

        var (coordinator, connection, _) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            coordinator.Pause();
            await coordinator.Completion;

            coordinator.Start(force: false);
            await coordinator.Completion;

            Assert.Equal(SyncPhase.Idle, coordinator.Status.Phase);
            Assert.Equal(2, new SqliteLibraryQuery(connection).GetSyncHistory(10, Start.AddHours(1)).Count);
        }
    }

    [Fact]
    public async Task AKeyRevokedMidRunFlagsTheKeyAndKeepsTheProgressOnScreen()
    {
        var runner = new FakeSyncRunner((progress, _) =>
        {
            progress!.Report(new SyncProgress(400, 1482, "Stellaris"));
            throw new SteamApiException(SteamApiErrorKind.InvalidKey, 401, "Steam rejected the API key. Check it in settings.");
        });

        var (coordinator, connection, accounts) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            await coordinator.Completion;

            Assert.Equal(SyncPhase.KeyRejected, coordinator.Status.Phase);
            Assert.Equal(400, coordinator.Status.Completed);
            Assert.NotNull(accounts.KeyRejectedAt);
            Assert.Equal(
                SyncRunOutcome.Failed,
                new SqliteLibraryQuery(connection).GetSyncHistory(10, Start.AddHours(1)).Single().Outcome);
        }
    }

    [Fact]
    public async Task ASuccessfulRunClearsAPreviousKeyRejection()
    {
        var (coordinator, connection, accounts) = Build(new FakeSyncRunner((_, _) => Task.CompletedTask));
        using (connection)
        using (coordinator)
        {
            accounts.MarkKeyRejected(Start);

            coordinator.Start(force: false);
            await coordinator.Completion;

            Assert.Null(accounts.KeyRejectedAt);
        }
    }

    [Fact]
    public async Task ANetworkFailureEndsInFailedRatherThanKeyRejected()
    {
        var runner = new FakeSyncRunner((_, _) =>
            throw new SteamApiException(SteamApiErrorKind.ServerError, 503, "Steam returned 503."));

        var (coordinator, connection, accounts) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            await coordinator.Completion;

            Assert.Equal(SyncPhase.Failed, coordinator.Status.Phase);
            Assert.Equal("Steam returned 503.", coordinator.Status.Error);
            Assert.Null(accounts.KeyRejectedAt);
        }
    }

    [Fact]
    public async Task RefusesToStartASecondRunWhileOneIsInFlight()
    {
        var starts = 0;
        var runner = new FakeSyncRunner((_, token) =>
        {
            starts++;
            return UntilCancelled(token);
        });

        var (coordinator, connection, _) = Build(runner);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            coordinator.Start(force: false);
            coordinator.Cancel();
            await coordinator.Completion;

            Assert.Equal(1, starts);
        }
    }

    [Fact]
    public async Task FailsImmediatelyWhenNoAccountIsConfigured()
    {
        var (coordinator, connection, _) = Build(new FakeSyncRunner((_, _) => Task.CompletedTask), withAccount: false);
        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            await coordinator.Completion;

            Assert.Equal(SyncPhase.Failed, coordinator.Status.Phase);
            Assert.Empty(new SqliteLibraryQuery(connection).GetSyncHistory(10, Start.AddHours(1)));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: FAIL — `SyncCoordinator`, `SyncPhase` and `ISyncRunner` could not be found.

- [ ] **Step 3: Declare the contracts the screens consume**

Create `SteamAchievements.Core/Presentation/SyncStatusView.cs`:

```csharp
namespace SteamAchievements.Core.Presentation;

/// <summary>
/// Every distinction the screens need about the sync, and no more.
///
/// <c>Paused</c> and <c>Idle</c> are both "not running" — the difference is
/// whether the user stopped it intending to come back, which decides whether the
/// button says "Resume" or "Sync". <c>KeyRejected</c> is kept apart from
/// <c>Failed</c> because it is the one failure with an action attached to it.
/// </summary>
public enum SyncPhase
{
    NeverRun,
    Idle,
    Running,
    Paused,
    Failed,
    KeyRejected,
}

public sealed record SyncStatusView(
    SyncPhase Phase,
    int Completed,
    int Total,
    string CurrentGame,
    string Headline,
    string? Detail,
    string? Error);

/// <summary>
/// What the sidebar card and the sync screen read.
///
/// <c>Changed</c> is raised from whichever thread the sync engine's progress
/// callback runs on, which is a worker thread, not the renderer. Components
/// must wrap their reaction in <c>InvokeAsync(StateHasChanged)</c>: reading
/// <c>ILibraryQuery</c> off the render thread is concurrent use of a single
/// SqliteConnection, which corrupts rather than throws.
/// </summary>
public interface ISyncPresenter
{
    SyncStatusView Status { get; }

    event Action? Changed;
}

/// <summary>
/// There is no <c>Resume</c>. Resuming is <c>Start(force: false)</c> — the sync
/// is resumable because progress is written per game — and the screen picks the
/// button's label from <see cref="SyncStatusView.Phase"/>.
/// </summary>
public interface ISyncController
{
    void Start(bool force);

    void Pause();

    void Cancel();
}
```

- [ ] **Step 4: Declare the runner seam and its live implementation**

Create `SteamAchievements.Core/Sync/ISyncRunner.cs`:

```csharp
namespace SteamAchievements.Core.Sync;

/// <summary>
/// One sync, start to finish. The seam exists so the state machine around it can
/// be tested against scripted progress and scripted failures instead of against
/// a real orchestrator, an HTTP fixture and a database.
/// </summary>
public interface ISyncRunner
{
    Task RunAsync(
        ulong steamId,
        bool force,
        IProgress<SyncProgress>? progress,
        CancellationToken cancellationToken);
}
```

Create `SteamAchievements.Core/Sync/LiveSyncRunner.cs`:

```csharp
using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Sync;

/// <summary>
/// Builds a <see cref="SyncOrchestrator"/> per run out of whatever key is
/// currently stored. Per run rather than once, because the user can replace the
/// key in settings and the next sync has to use the new one without restarting
/// the application.
/// </summary>
public sealed class LiveSyncRunner : ISyncRunner
{
    private readonly ISecretStore _secrets;
    private readonly GameRepository _repository;
    private readonly Func<string, SteamApiClient> _clientFactory;

    public LiveSyncRunner(
        ISecretStore secrets, GameRepository repository, Func<string, SteamApiClient> clientFactory)
    {
        _secrets = secrets;
        _repository = repository;
        _clientFactory = clientFactory;
    }

    public async Task RunAsync(
        ulong steamId, bool force, IProgress<SyncProgress>? progress, CancellationToken cancellationToken)
    {
        var key = _secrets.Read();

        if (string.IsNullOrEmpty(key))
        {
            // Reported as InvalidKey rather than as its own kind so the state
            // machine lands on KeyRejected, which is the screen that lets the
            // user do something about it.
            throw new SteamApiException(
                SteamApiErrorKind.InvalidKey, 0, "No Steam API key is stored. Add one in settings.");
        }

        var orchestrator = new SyncOrchestrator(_clientFactory(key), _repository, SyncOptions.Default);

        await orchestrator.RunAsync(steamId, force, progress, cancellationToken);
    }
}
```

- [ ] **Step 5: Write the coordinator**

Create `SteamAchievements.Core/App/SyncCoordinator.cs`:

```csharp
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Sync;

namespace SteamAchievements.Core.App;

/// <summary>
/// Owns everything about a sync that is not the sync itself: which phase the UI
/// is in, the cancellation token, the journal row, and the key-rejection flag.
///
/// Deliberately depends on <see cref="ISyncRunner"/> rather than on
/// <c>SyncOrchestrator</c>, so every branch below is reachable from a unit test.
/// </summary>
public sealed class SyncCoordinator : ISyncPresenter, ISyncController, IDisposable
{
    /// <summary>
    /// Invokes the handler on the calling thread. <see cref="Progress{T}"/>
    /// captures the SynchronizationContext at construction and posts
    /// asynchronously, which would make the recorded <c>games_synced</c> lag
    /// behind the run it belongs to.
    /// </summary>
    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public InlineProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }

    private readonly ISyncRunner _runner;
    private readonly IAccountStore _accounts;
    private readonly SyncJournal _journal;
    private readonly Func<DateTimeOffset> _now;
    private readonly Lock _gate = new();

    private CancellationTokenSource? _cancellation;
    private bool _pausing;
    private SyncStatusView _status;
    private Task _completion = Task.CompletedTask;

    public SyncCoordinator(
        ISyncRunner runner, IAccountStore accounts, SyncJournal journal, Func<DateTimeOffset> now)
    {
        _runner = runner;
        _accounts = accounts;
        _journal = journal;
        _now = now;

        _status = _accounts.KeyRejectedAt is not null
            ? new SyncStatusView(SyncPhase.KeyRejected, 0, 0, string.Empty,
                "Steam rejected the API key", "Replace it in settings to continue.", null)
            : _journal.LastSyncedAt is null
                ? new SyncStatusView(SyncPhase.NeverRun, 0, 0, string.Empty, "Never synced", null, null)
                : new SyncStatusView(SyncPhase.Idle, 0, 0, string.Empty, "Up to date", null, null);
    }

    public SyncStatusView Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public event Action? Changed;

    /// <summary>
    /// Completes when the in-flight run does, or immediately when none is. Public
    /// because the host has to await it during shutdown: disposing the SQLite
    /// connections while the orchestrator is still writing to them is a use of a
    /// disposed connection from a worker thread.
    /// </summary>
    public Task Completion
    {
        get
        {
            lock (_gate)
            {
                return _completion;
            }
        }
    }

    public void Start(bool force)
    {
        StoredAccount? account;

        lock (_gate)
        {
            if (_status.Phase == SyncPhase.Running)
            {
                return;
            }

            account = _accounts.Current;

            if (account is not null)
            {
                _pausing = false;
                _cancellation = new CancellationTokenSource();
                _status = new SyncStatusView(SyncPhase.Running, 0, 0, string.Empty, "Starting…", null, null);
                _completion = RunAsync(account.SteamId64, force, _cancellation.Token);
            }
        }

        if (account is null)
        {
            Publish(new SyncStatusView(SyncPhase.Failed, 0, 0, string.Empty,
                "Sync failed", null, "No Steam account is configured."));
            return;
        }

        Changed?.Invoke();
    }

    public void Pause() => Stop(pausing: true);

    public void Cancel() => Stop(pausing: false);

    private void Stop(bool pausing)
    {
        lock (_gate)
        {
            if (_status.Phase != SyncPhase.Running)
            {
                return;
            }

            _pausing = pausing;
            _cancellation?.Cancel();
        }
    }

    private async Task RunAsync(ulong steamId, bool force, CancellationToken cancellationToken)
    {
        var startedAt = _now();
        var kind = force ? "full" : "incremental";
        var completed = 0;
        var total = 0;

        var progress = new InlineProgress<SyncProgress>(report =>
        {
            completed = report.Completed;
            total = report.Total;

            Publish(new SyncStatusView(
                SyncPhase.Running, report.Completed, report.Total, report.CurrentGame,
                $"Syncing {report.Completed} of {report.Total}", report.CurrentGame, null));
        });

        try
        {
            await _runner.RunAsync(steamId, force, progress, cancellationToken);

            var finishedAt = _now();
            _accounts.ClearKeyRejected();
            _journal.RecordRun(new SyncRunRecord(startedAt, kind, completed, Elapsed(startedAt, finishedAt), null));
            _journal.MarkSyncCompleted(finishedAt);

            Publish(new SyncStatusView(SyncPhase.Idle, completed, total, string.Empty, "Up to date", null, null));
        }
        catch (OperationCanceledException)
        {
            var paused = Paused();
            _journal.RecordRun(new SyncRunRecord(
                startedAt, kind, completed, Elapsed(startedAt, _now()), SyncJournal.Cancelled));

            Publish(new SyncStatusView(
                paused ? SyncPhase.Paused : SyncPhase.Idle, completed, total, string.Empty,
                paused ? $"Paused at {completed} of {total}" : "Sync cancelled", null, null));
        }
        catch (SteamApiException e) when (e.Kind == SteamApiErrorKind.InvalidKey)
        {
            var finishedAt = _now();
            _accounts.MarkKeyRejected(finishedAt);
            _journal.RecordRun(new SyncRunRecord(startedAt, kind, completed, Elapsed(startedAt, finishedAt), e.Message));

            Publish(new SyncStatusView(
                SyncPhase.KeyRejected, completed, total, string.Empty,
                "Steam rejected the API key", "Replace it in settings to continue.", e.Message));
        }
        catch (Exception e)
        {
            _journal.RecordRun(new SyncRunRecord(
                startedAt, kind, completed, Elapsed(startedAt, _now()), e.Message));

            Publish(new SyncStatusView(
                SyncPhase.Failed, completed, total, string.Empty, "Sync failed", null, e.Message));
        }
        finally
        {
            lock (_gate)
            {
                _cancellation?.Dispose();
                _cancellation = null;
            }
        }
    }

    private bool Paused()
    {
        lock (_gate)
        {
            return _pausing;
        }
    }

    private static long Elapsed(DateTimeOffset from, DateTimeOffset to) => (long)(to - from).TotalMilliseconds;

    /// <summary>
    /// Assigns under the lock and raises outside it. Raising while holding the
    /// lock would let a handler that reads <see cref="Status"/> re-enter it.
    /// </summary>
    private void Publish(SyncStatusView status)
    {
        lock (_gate)
        {
            _status = status;
        }

        Changed?.Invoke();
    }

    public void Dispose()
    {
        Cancel();

        // Bounded rather than indefinite: shutdown must not hang on a sync that
        // refuses to notice its cancellation.
        Completion.Wait(TimeSpan.FromSeconds(5));

        lock (_gate)
        {
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: PASS, 15 new tests.

If `PausingLeavesTheProgressVisible...` is flaky, the cause is `Start` returning before the fake has reported progress. It cannot: the fake reports synchronously before returning its waiting task, and `InlineProgress` invokes on the calling thread. If it does fail, do not add a sleep — find out which of those two properties stopped holding.

- [ ] **Step 7: Commit**

```bash
git add SteamAchievements.Core/Presentation/SyncStatusView.cs \
        SteamAchievements.Core/Sync/ISyncRunner.cs \
        SteamAchievements.Core/Sync/LiveSyncRunner.cs \
        SteamAchievements.Core/App/SyncCoordinator.cs \
        SteamAchievements.Core.Tests/App/SyncCoordinatorTests.cs
git commit -m "feat: add the sync state machine behind a testable runner seam"
```

---

## Task 8: Onboarding

**Files:**
- Create: `SteamAchievements.Core/App/SteamId.cs`
- Create: `SteamAchievements.Core/Presentation/IExternalLinks.cs`
- Create: `SteamAchievements.Core/Presentation/IOnboarding.cs`
- Create: `SteamAchievements.Core/App/OnboardingService.cs`
- Test: `SteamAchievements.Core.Tests/App/SteamIdTests.cs`
- Test: `SteamAchievements.Core.Tests/App/OnboardingServiceTests.cs`

**Interfaces:**
- Consumes: `ApiKey`, `ISecretStore`, `DataPaths` (Task 1), `IAccountStore` (Task 2), `OnboardingState`, `OnboardingStep` (Task 5), `SteamAccountLocator` (Task 5), `SteamCommunityClient`, `PublicProfile` (Task 6), `SteamAccount`, `SteamApiClient`, `SteamApiException`, `SteamApiErrorKind` (pre-existing).
- Produces: `SteamId.TryParse(string? candidate, out ulong steamId)`. `IExternalLinks` with `void OpenApiKeyPage()`, `void OpenDataFolder()`, `void OpenUrl(string url)`. `KeySubmission { Malformed, Rejected, Unreachable, Accepted }`. `IOnboarding` with `OnboardingStep Step { get; }`, `IReadOnlyList<SteamAccount> DiscoveredAccounts { get; }`, `Task ChooseAccountAsync(ulong steamId64, CancellationToken ct)`, `Task<KeySubmission> SubmitKeyAsync(string pasted, CancellationToken ct)`, `event Action? Changed`.

A key is checked against Steam before it is stored. One `GetOwnedGames` call answers "is this key any good" in under a second; without it the user finds out several minutes into their first sync. This is the "trial request" step §5.4 of the spec asks for and the reason the error table has a separate row for a key rejected during onboarding.

- [ ] **Step 1: Write the failing tests**

Create `SteamAchievements.Core.Tests/App/SteamIdTests.cs`:

```csharp
using SteamAchievements.Core.App;

namespace SteamAchievements.Core.Tests.App;

public class SteamIdTests
{
    [Fact]
    public void AcceptsASeventeenDigitId()
    {
        Assert.True(SteamId.TryParse("76561190000000002", out var parsed));
        Assert.Equal(76561190000000002UL, parsed);
    }

    [Fact]
    public void AcceptsAProfileUrl()
    {
        Assert.True(SteamId.TryParse("https://steamcommunity.com/profiles/76561190000000002/", out var parsed));
        Assert.Equal(76561190000000002UL, parsed);
    }

    [Fact]
    public void AcceptsAProfileUrlWithoutATrailingSlash()
    {
        Assert.True(SteamId.TryParse("https://steamcommunity.com/profiles/76561190000000002", out var parsed));
        Assert.Equal(76561190000000002UL, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7656119")]
    [InlineData("not a number")]
    [InlineData("https://steamcommunity.com/id/oustrix")]
    public void RejectsAnythingElseIncludingVanityUrls(string? candidate)
    {
        Assert.False(SteamId.TryParse(candidate, out var parsed));
        Assert.Equal(0UL, parsed);
    }
}
```

Create `SteamAchievements.Core.Tests/App/OnboardingServiceTests.cs`:

```csharp
using System.Net;
using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.App;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Tests.Steam;

namespace SteamAchievements.Core.Tests.App;

public class OnboardingServiceTests
{
    private static readonly ulong SteamId = 76561190000000002;
    private const string Key = "0123456789abcdef0123456789abcdef";

    private sealed class MemorySecretStore : ISecretStore
    {
        private string? _secret;

        public string? Read() => _secret;

        public void Write(string secret) => _secret = secret;

        public void Clear() => _secret = null;
    }

    private sealed class NoSteam : ISteamPathProvider
    {
        public string? FindSteamPath() => null;
    }

    /// <param name="keyCheck">
    /// How Steam answers the trial GetOwnedGames request that validates a
    /// submitted key. Defaults to the recorded success fixture.
    /// </param>
    private static async Task<(OnboardingService Service, MemorySecretStore Secrets, IAccountStore Accounts, Microsoft.Data.Sqlite.SqliteConnection Connection)>
        BuildAsync(
            HttpStatusCode status = HttpStatusCode.OK,
            string? body = null,
            FakeHttpMessageHandler? keyCheck = null)
    {
        body ??= await File.ReadAllTextAsync(TestPaths.Data("profile_public.xml"));

        var connection = Database.Open(":memory:");
        var accounts = new SqliteAccountStore(connection);
        var secrets = new MemorySecretStore();

        var community = new SteamCommunityClient(
            new HttpClient(FakeHttpMessageHandler.Returning(status, body, "text/xml"))
            {
                BaseAddress = new Uri("https://steamcommunity.com/"),
            });

        keyCheck ??= FakeHttpMessageHandler.Returning(
            HttpStatusCode.OK, await File.ReadAllTextAsync(TestPaths.Data("owned_games.json")));

        SteamApiClient ClientFor(string key) =>
            new(new HttpClient(keyCheck) { BaseAddress = new Uri("https://api.steampowered.com/") }, key);

        return (
            new OnboardingService(accounts, secrets, new SteamAccountLocator(new NoSteam()), community, ClientFor),
            secrets, accounts, connection);
    }

    [Fact]
    public async Task StartsAtAccountSelection()
    {
        var (service, _, _, connection) = await BuildAsync();
        using (connection)
        {
            Assert.Equal(OnboardingStep.ChooseAccount, service.Step);
        }
    }

    [Fact]
    public async Task StoresTheChosenAccountWithItsNameAndAvatar()
    {
        var (service, _, accounts, connection) = await BuildAsync();
        using (connection)
        {
            await service.ChooseAccountAsync(SteamId, CancellationToken.None);

            var stored = accounts.Current;
            Assert.NotNull(stored);
            Assert.Equal(SteamId, stored.SteamId64);
            Assert.Equal("oustrix", stored.PersonaName);
            Assert.EndsWith("_full.jpg", stored.AvatarUrl);
        }
    }

    [Fact]
    public async Task StoresTheAccountEvenWhenTheProfileLookupFails()
    {
        var (service, _, accounts, connection) = await BuildAsync(HttpStatusCode.ServiceUnavailable, string.Empty);
        using (connection)
        {
            await service.ChooseAccountAsync(SteamId, CancellationToken.None);

            Assert.Equal(SteamId, accounts.Current!.SteamId64);
            Assert.Equal(string.Empty, accounts.Current.PersonaName);
        }
    }

    [Fact]
    public async Task MovesToTheKeyStepOnceAnAccountIsChosen()
    {
        var (service, _, _, connection) = await BuildAsync();
        using (connection)
        {
            await service.ChooseAccountAsync(SteamId, CancellationToken.None);

            Assert.Equal(OnboardingStep.EnterKey, service.Step);
        }
    }

    [Fact]
    public async Task StoresANormalizedKeyOnceSteamAcceptsItAndFinishesOnboarding()
    {
        var (service, secrets, _, connection) = await BuildAsync();
        using (connection)
        {
            await service.ChooseAccountAsync(SteamId, CancellationToken.None);

            Assert.Equal(KeySubmission.Accepted, await service.SubmitKeyAsync($"  {Key}  ", CancellationToken.None));
            Assert.Equal(Key.ToUpperInvariant(), secrets.Read());
            Assert.Equal(OnboardingStep.Ready, service.Step);
        }
    }

    [Fact]
    public async Task RejectsAMalformedKeyWithoutSpendingARequest()
    {
        var keyCheck = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("must not be called"));
        var (service, secrets, _, connection) = await BuildAsync(keyCheck: keyCheck);
        using (connection)
        {
            await service.ChooseAccountAsync(SteamId, CancellationToken.None);

            Assert.Equal(KeySubmission.Malformed, await service.SubmitKeyAsync("nope", CancellationToken.None));
            Assert.Null(secrets.Read());
            Assert.Empty(keyCheck.Requests);
            Assert.Equal(OnboardingStep.EnterKey, service.Step);
        }
    }

    [Fact]
    public async Task DoesNotStoreAWellFormedKeySteamRefuses()
    {
        var html = await File.ReadAllTextAsync(TestPaths.Data("error_unauthorized.html"));
        var (service, secrets, _, connection) = await BuildAsync(
            keyCheck: FakeHttpMessageHandler.Returning(HttpStatusCode.Unauthorized, html, "text/html"));

        using (connection)
        {
            await service.ChooseAccountAsync(SteamId, CancellationToken.None);

            Assert.Equal(KeySubmission.Rejected, await service.SubmitKeyAsync(Key, CancellationToken.None));
            Assert.Null(secrets.Read());
            Assert.Equal(OnboardingStep.EnterKey, service.Step);
        }
    }

    [Fact]
    public async Task ReportsAnUnreachableSteamSeparatelyFromARefusedKey()
    {
        var (service, secrets, _, connection) = await BuildAsync(
            keyCheck: new FakeHttpMessageHandler(_ => throw new HttpRequestException("no route")));

        using (connection)
        {
            await service.ChooseAccountAsync(SteamId, CancellationToken.None);

            Assert.Equal(KeySubmission.Unreachable, await service.SubmitKeyAsync(Key, CancellationToken.None));
            Assert.Null(secrets.Read());
        }
    }

    [Fact]
    public async Task AcceptingAKeyClearsAnEarlierRejection()
    {
        var (service, _, accounts, connection) = await BuildAsync();
        using (connection)
        {
            await service.ChooseAccountAsync(SteamId, CancellationToken.None);
            accounts.MarkKeyRejected(DateTimeOffset.UtcNow);

            await service.SubmitKeyAsync(Key, CancellationToken.None);

            Assert.Null(accounts.KeyRejectedAt);
        }
    }

    [Fact]
    public async Task RefusesToValidateAKeyBeforeAnAccountIsChosen()
    {
        var (service, _, _, connection) = await BuildAsync();
        using (connection)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SubmitKeyAsync(Key, CancellationToken.None));
        }
    }

    [Fact]
    public async Task FindsNoAccountsWhenSteamIsAbsentSoTheScreenFallsBackToManualEntry()
    {
        var (service, _, _, connection) = await BuildAsync();
        using (connection)
        {
            Assert.Empty(service.DiscoveredAccounts);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: FAIL — `SteamId` and `OnboardingService` could not be found.

- [ ] **Step 3: Write the SteamID parser**

Create `SteamAchievements.Core/App/SteamId.cs`:

```csharp
namespace SteamAchievements.Core.App;

/// <summary>
/// Manual entry for when Steam is not installed, or the user is not signed into
/// it on this machine.
/// </summary>
public static class SteamId
{
    private const string ProfilesSegment = "/profiles/";

    /// <summary>
    /// Accepts a 17-digit SteamID64 or a <c>/profiles/&lt;id&gt;</c> URL.
    ///
    /// Vanity URLs (<c>/id/&lt;name&gt;</c>) are deliberately rejected: resolving
    /// one needs an endpoint that is not in docs/steam-api.md, and adding an
    /// endpoint means verifying it against live requests first.
    /// </summary>
    public static bool TryParse(string? candidate, out ulong steamId)
    {
        steamId = 0;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var text = candidate.Trim();
        var segment = text.IndexOf(ProfilesSegment, StringComparison.OrdinalIgnoreCase);

        if (segment >= 0)
        {
            text = text[(segment + ProfilesSegment.Length)..].Trim('/');
        }

        // A SteamID64 is always 17 digits. Length is checked as well as
        // parseability so a truncated paste is rejected rather than accepted as
        // a small number.
        if (text.Length != 17 || !ulong.TryParse(text, out var parsed))
        {
            return false;
        }

        steamId = parsed;
        return true;
    }
}
```

- [ ] **Step 4: Declare the screen-facing contracts**

Create `SteamAchievements.Core/Presentation/IExternalLinks.cs`:

```csharp
namespace SteamAchievements.Core.Presentation;

/// <summary>
/// Anything that leaves the application window. Implemented in the WPF project
/// with <c>Process.Start</c>; declared here so the screens do not reference it.
/// </summary>
public interface IExternalLinks
{
    /// <summary>Steam's API key issuance page.</summary>
    void OpenApiKeyPage();

    /// <summary>The folder holding the database and the stored key.</summary>
    void OpenDataFolder();

    void OpenUrl(string url);
}
```

Create `SteamAchievements.Core/Presentation/IOnboarding.cs`:

```csharp
using SteamAchievements.Core.App;
using SteamAchievements.Core.Local;

namespace SteamAchievements.Core.Presentation;

/// <summary>How far a submitted key got. Only <c>Accepted</c> stores anything.</summary>
public enum KeySubmission
{
    /// <summary>Not 32 hexadecimal characters. No request was made.</summary>
    Malformed,

    /// <summary>Well-formed, and Steam refused it.</summary>
    Rejected,

    /// <summary>Steam could not be asked. Distinct from a refusal: retrying is the right advice.</summary>
    Unreachable,

    Accepted,
}

/// <summary>
/// What the onboarding screen talks to. <c>Changed</c> follows the same rule as
/// <see cref="ISyncPresenter.Changed"/>: wrap the reaction in
/// <c>InvokeAsync(StateHasChanged)</c>.
/// </summary>
public interface IOnboarding
{
    OnboardingStep Step { get; }

    /// <summary>Accounts signed into Steam on this machine. Empty is normal, not an error.</summary>
    IReadOnlyList<SteamAccount> DiscoveredAccounts { get; }

    /// <summary>
    /// Stores the account, filling in the persona name and avatar if the public
    /// profile answers. A profile lookup failure never blocks this.
    /// </summary>
    Task ChooseAccountAsync(ulong steamId64, CancellationToken cancellationToken);

    /// <summary>
    /// Checks the format, then checks the key against Steam, then stores it.
    /// Nothing is stored unless the result is <see cref="KeySubmission.Accepted"/>.
    /// Throws <see cref="InvalidOperationException"/> if no account has been
    /// chosen yet — that is a screen ordering bug, not a user state.
    /// </summary>
    Task<KeySubmission> SubmitKeyAsync(string pasted, CancellationToken cancellationToken);

    event Action? Changed;
}
```

- [ ] **Step 5: Write the service**

Create `SteamAchievements.Core/App/OnboardingService.cs`:

```csharp
using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Local;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.App;

public sealed class OnboardingService : IOnboarding
{
    private readonly IAccountStore _accounts;
    private readonly ISecretStore _secrets;
    private readonly SteamAccountLocator _locator;
    private readonly SteamCommunityClient _community;
    private readonly Func<string, SteamApiClient> _clientFactory;

    public OnboardingService(
        IAccountStore accounts,
        ISecretStore secrets,
        SteamAccountLocator locator,
        SteamCommunityClient community,
        Func<string, SteamApiClient> clientFactory)
    {
        _accounts = accounts;
        _secrets = secrets;
        _locator = locator;
        _community = community;
        _clientFactory = clientFactory;
    }

    public OnboardingStep Step =>
        OnboardingState.Evaluate(_accounts.Current?.SteamId64, !string.IsNullOrEmpty(_secrets.Read()));

    public IReadOnlyList<SteamAccount> DiscoveredAccounts => _locator.FindAccounts();

    public event Action? Changed;

    public async Task ChooseAccountAsync(ulong steamId64, CancellationToken cancellationToken)
    {
        // The name and the picture are decoration. If the community site is
        // down, redirects, or the profile does not exist, the user still gets
        // through onboarding with a bare SteamID.
        var profile = await _community.GetProfileAsync(steamId64, cancellationToken);

        _accounts.Set(steamId64, profile?.PersonaName ?? string.Empty, profile?.AvatarUrl ?? string.Empty);

        Changed?.Invoke();
    }

    public async Task<KeySubmission> SubmitKeyAsync(string pasted, CancellationToken cancellationToken)
    {
        // Format first, so a typo costs nothing.
        if (!ApiKey.TryNormalize(pasted, out var normalized))
        {
            return KeySubmission.Malformed;
        }

        var account = _accounts.Current
            ?? throw new InvalidOperationException("An account must be chosen before a key can be checked.");

        try
        {
            // One cheap call that answers "is this key any good". Without it the
            // user finds out several minutes into their first sync instead.
            await _clientFactory(normalized).GetOwnedGamesAsync(account.SteamId64, cancellationToken);
        }
        catch (SteamApiException e) when (e.Kind == SteamApiErrorKind.InvalidKey)
        {
            return KeySubmission.Rejected;
        }
        catch (SteamApiException)
        {
            // Rate limited, a 5xx, or a transport failure the client already
            // folded into ServerError. The key may well be fine, so it is not
            // called rejected and it is not stored either.
            return KeySubmission.Unreachable;
        }

        _secrets.Write(normalized);

        // A new key deserves a clean slate: whatever made the previous one look
        // rejected has nothing to do with this one.
        _accounts.ClearKeyRejected();

        Changed?.Invoke();
        return KeySubmission.Accepted;
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add SteamAchievements.Core/App/SteamId.cs \
        SteamAchievements.Core/App/OnboardingService.cs \
        SteamAchievements.Core/Presentation/IExternalLinks.cs \
        SteamAchievements.Core/Presentation/IOnboarding.cs \
        SteamAchievements.Core.Tests/App
git commit -m "feat: add onboarding over account discovery, profile lookup and the key"
```

---

## Task 9: Switching accounts and resetting

**Files:**
- Create: `SteamAchievements.Core/Presentation/IAccountAdmin.cs`
- Create: `SteamAchievements.Core/App/AccountAdminService.cs`
- Test: `SteamAchievements.Core.Tests/App/AccountAdminServiceTests.cs`

**Interfaces:**
- Consumes: `IAccountStore`, `StoredAccount` (Task 2), `Database.ResetLibrary` (Task 3), `SteamAccountLocator` (Task 5), `ISecretStore` (Task 1), `SteamCommunityClient` (Task 6), `ILibraryQuery` (pre-existing).
- Produces: `AccountMismatch(ulong ActiveSteamId64, string ActiveAccountName)`. `IAccountAdmin` with `StoredAccount? Current { get; }`, `AccountMismatch? Mismatch { get; }`, `Task SwitchToAsync(ulong steamId64, CancellationToken ct)`, `void ResetEverything()`, `event Action? Changed`.

- [ ] **Step 1: Write the failing test**

Create `SteamAchievements.Core.Tests/App/AccountAdminServiceTests.cs`:

```csharp
using System.Net;
using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.App;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Tests.Steam;

namespace SteamAchievements.Core.Tests.App;

public class AccountAdminServiceTests
{
    // Deliberately not 76561190000000002: that is the account the committed
    // loginusers.vdf fixture marks MostRecent, and the mismatch tests below need
    // the stored account to differ from the active one.
    private static readonly ulong Stored = 76561190000000009;
    private static readonly ulong Other = 76561190000000003;
    private static readonly ulong ActiveInFixture = 76561190000000002;

    private sealed class MemorySecretStore : ISecretStore
    {
        public string? Secret { get; private set; } = "0123456789ABCDEF0123456789ABCDEF";

        public string? Read() => Secret;

        public void Write(string secret) => Secret = secret;

        public void Clear() => Secret = null;
    }

    private sealed class FixedPath(string? path) : ISteamPathProvider
    {
        public string? FindSteamPath() => path;
    }

    private static string SteamRootWithFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.Copy(TestPaths.Data("loginusers.vdf"), Path.Combine(root, "config", "loginusers.vdf"));
        return root;
    }

    private static async Task<(AccountAdminService Admin, MemorySecretStore Secrets, IAccountStore Accounts, Microsoft.Data.Sqlite.SqliteConnection Connection)>
        BuildAsync(string? steamPath = null)
    {
        var body = await File.ReadAllTextAsync(TestPaths.Data("profile_public.xml"));
        var connection = Database.Open(":memory:");
        var accounts = new SqliteAccountStore(connection);
        accounts.Set(Stored, "oustrix", "avatar");

        var repository = new GameRepository(connection);
        repository.UpsertOwnedGames([new OwnedGame(220, "Half-Life 2", "hash", 600, 0, null)]);

        var secrets = new MemorySecretStore();
        var community = new SteamCommunityClient(
            new HttpClient(FakeHttpMessageHandler.Returning(HttpStatusCode.OK, body, "text/xml"))
            {
                BaseAddress = new Uri("https://steamcommunity.com/"),
            });

        var admin = new AccountAdminService(
            connection, accounts, secrets, new SteamAccountLocator(new FixedPath(steamPath)), community);

        return (admin, secrets, accounts, connection);
    }

    [Fact]
    public async Task ReportsTheStoredAccount()
    {
        var (admin, _, _, connection) = await BuildAsync();
        using (connection)
        {
            Assert.Equal(Stored, admin.Current!.SteamId64);
        }
    }

    [Fact]
    public async Task ReportsNoMismatchWhenSteamIsNotInstalled()
    {
        var (admin, _, _, connection) = await BuildAsync();
        using (connection)
        {
            Assert.Null(admin.Mismatch);
        }
    }

    [Fact]
    public async Task ReportsAMismatchWhenSteamIsSignedInAsSomebodyElse()
    {
        var root = SteamRootWithFixture();
        try
        {
            var (admin, _, _, connection) = await BuildAsync(root);
            using (connection)
            {
                Assert.NotNull(admin.Mismatch);
                Assert.Equal(ActiveInFixture, admin.Mismatch.ActiveSteamId64);
                Assert.Equal("currentuser", admin.Mismatch.ActiveAccountName);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsNoMismatchWhenSteamIsSignedInAsTheStoredAccount()
    {
        var root = SteamRootWithFixture();
        try
        {
            var (admin, _, accounts, connection) = await BuildAsync(root);
            using (connection)
            {
                accounts.Set(ActiveInFixture, "oustrix", "avatar");

                Assert.Null(admin.Mismatch);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SwitchingEmptiesTheLibraryAndStoresTheNewAccount()
    {
        var (admin, _, accounts, connection) = await BuildAsync();
        using (connection)
        {
            await admin.SwitchToAsync(Other, CancellationToken.None);

            Assert.Equal(Other, accounts.Current!.SteamId64);
            Assert.Equal(0, Dapper.SqlMapper.QuerySingle<long>(connection, "SELECT COUNT(*) FROM owned_games"));
        }
    }

    [Fact]
    public async Task SwitchingKeepsTheKeyBecauseItIsNotBoundToAnAccount()
    {
        var (admin, secrets, _, connection) = await BuildAsync();
        using (connection)
        {
            await admin.SwitchToAsync(Other, CancellationToken.None);

            Assert.NotNull(secrets.Secret);
        }
    }

    [Fact]
    public async Task ResettingEverythingAlsoDiscardsTheKey()
    {
        var (admin, secrets, accounts, connection) = await BuildAsync();
        using (connection)
        {
            admin.ResetEverything();

            Assert.Null(secrets.Secret);
            Assert.Null(accounts.Current);
            Assert.Equal(0, Dapper.SqlMapper.QuerySingle<long>(connection, "SELECT COUNT(*) FROM owned_games"));
        }
    }

    [Fact]
    public async Task ResettingKeepsTheAccent()
    {
        var (admin, _, _, connection) = await BuildAsync();
        using (connection)
        {
            new SqliteUserPreferences(connection).SetAccent("#c98f7a");

            admin.ResetEverything();

            Assert.Equal("#c98f7a", new SqliteUserPreferences(connection).Accent);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: FAIL — `AccountAdminService` could not be found.

- [ ] **Step 3: Declare the contract**

Create `SteamAchievements.Core/Presentation/IAccountAdmin.cs`:

```csharp
using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Presentation;

/// <summary>
/// Steam is signed in as somebody other than the account this database belongs
/// to. An observation, not an error: the stored account stays authoritative and
/// nothing happens until the user asks for it.
/// </summary>
public sealed record AccountMismatch(ulong ActiveSteamId64, string ActiveAccountName);

public interface IAccountAdmin
{
    StoredAccount? Current { get; }

    /// <summary>Null when Steam is absent, or signed in as the stored account.</summary>
    AccountMismatch? Mismatch { get; }

    /// <summary>
    /// Empties the library and stores the new account. Destructive, and the
    /// screen must confirm before calling it. The API key is kept — a Steam key
    /// is not bound to an account.
    /// </summary>
    Task SwitchToAsync(ulong steamId64, CancellationToken cancellationToken);

    /// <summary>Everything <see cref="SwitchToAsync"/> clears, plus the stored key.</summary>
    void ResetEverything();

    event Action? Changed;
}
```

- [ ] **Step 4: Write the service**

Create `SteamAchievements.Core/App/AccountAdminService.cs`:

```csharp
using Microsoft.Data.Sqlite;
using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.App;

/// <summary>
/// Switching accounts and resetting are the same database operation — no table
/// carries a SteamID column, so a library belongs to exactly one account. The
/// difference is visible here rather than hidden in a flag: switching keeps the
/// key, resetting discards it.
/// </summary>
public sealed class AccountAdminService : IAccountAdmin
{
    private readonly SqliteConnection _connection;
    private readonly IAccountStore _accounts;
    private readonly ISecretStore _secrets;
    private readonly SteamAccountLocator _locator;
    private readonly SteamCommunityClient _community;

    /// <param name="connection">
    /// The settings connection. <c>ResetLibrary</c> opens a transaction, so this
    /// must be a writable connection carrying a busy timeout.
    /// </param>
    public AccountAdminService(
        SqliteConnection connection,
        IAccountStore accounts,
        ISecretStore secrets,
        SteamAccountLocator locator,
        SteamCommunityClient community)
    {
        _connection = connection;
        _accounts = accounts;
        _secrets = secrets;
        _locator = locator;
        _community = community;
    }

    public StoredAccount? Current => _accounts.Current;

    public AccountMismatch? Mismatch
    {
        get
        {
            var active = _locator.FindActiveAccount();
            var stored = _accounts.Current;

            if (active is null || stored is null || active.SteamId64 == stored.SteamId64)
            {
                return null;
            }

            return new AccountMismatch(active.SteamId64, active.AccountName);
        }
    }

    public event Action? Changed;

    public async Task SwitchToAsync(ulong steamId64, CancellationToken cancellationToken)
    {
        var profile = await _community.GetProfileAsync(steamId64, cancellationToken);

        Database.ResetLibrary(_connection);
        _accounts.Set(steamId64, profile?.PersonaName ?? string.Empty, profile?.AvatarUrl ?? string.Empty);

        Changed?.Invoke();
    }

    public void ResetEverything()
    {
        Database.ResetLibrary(_connection);
        _secrets.Clear();

        Changed?.Invoke();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: PASS.

Do not edit `testdata/loginusers.vdf` to make a test pass. It is a recorded sample and other tests assert against its exact contents.

- [ ] **Step 6: Commit**

```bash
git add SteamAchievements.Core/Presentation/IAccountAdmin.cs \
        SteamAchievements.Core/App/AccountAdminService.cs \
        SteamAchievements.Core.Tests/App/AccountAdminServiceTests.cs
git commit -m "feat: switch accounts and reset the library behind a confirmation"
```

---

## Task 10: The Windows implementations

**Everything from here on cannot be compiled or run on macOS.** Write it carefully, verify by CI. `dotnet build SteamAchievements.Core` still has to pass — run it to confirm nothing in Core was disturbed.

**Files:**
- Modify: `SteamAchievements.Windows/SteamAchievements.Windows.csproj`
- Create: `SteamAchievements.Windows/RegistrySteamPathProvider.cs`
- Create: `SteamAchievements.Windows/DpapiSecretStore.cs`
- Create: `SteamAchievements.Windows/ShellLinks.cs`
- Create: `SteamAchievements.Windows/WebView2Probe.cs`

**Interfaces:**
- Consumes: `ISteamPathProvider` (pre-existing), `ISecretStore` (Task 1), `IExternalLinks` (Task 8).
- Produces: `RegistrySteamPathProvider()`, `DpapiSecretStore(string path)`, `ShellLinks(string dataFolder)`, `WebView2Probe.IsRuntimeInstalled()`.

- [ ] **Step 1: Rewrite the project file**

Replace the whole of `SteamAchievements.Windows/SteamAchievements.Windows.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">

  <!--
    Microsoft.NET.Sdk.Razor, not Microsoft.NET.Sdk: this project contains
    .razor components (Components/Routes.razor) that the plain SDK does not
    compile. UseWPF still applies; this is the combination the official
    WPF + Blazor template uses.
  -->
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
  </PropertyGroup>

  <PropertyGroup>
    <!--
      BlazorWebView serves wwwroot/index.html and the RCL's _content assets from
      disk. PublishSingleFile bundles assemblies and, with
      IncludeNativeLibrariesForSelfExtract, native libraries — but not content.
      Without this the publish output is an exe plus a wwwroot tree.
    -->
    <IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>

    <!--
      Microsoft.Web.WebView2's Common.targets adds WebView2Loader.dll as a
      Content item linked into runtimes\win-x64\native\, separately from the RID
      asset PublishSingleFile already bundles. That copy would land loose in a
      subdirectory. This property is the package's own documented opt-out.
    -->
    <WebView2NeverCopyLoaderDllToOutputDirectory>true</WebView2NeverCopyLoaderDllToOutputDirectory>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebView.Wpf" Version="10.0.90" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.10" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\SteamAchievements.Core\SteamAchievements.Core.csproj" />
    <ProjectReference Include="..\SteamAchievements.UI\SteamAchievements.UI.csproj" />
  </ItemGroup>

  <!--
    A non-Web SDK does not treat wwwroot as content automatically, so the host
    page would never reach the output directory.
  -->
  <ItemGroup>
    <Content Include="wwwroot\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write the four Windows classes**

Create `SteamAchievements.Windows/RegistrySteamPathProvider.cs`:

```csharp
using Microsoft.Win32;
using SteamAchievements.Core.Abstractions;

namespace SteamAchievements.Windows;

public sealed class RegistrySteamPathProvider : ISteamPathProvider
{
    public string? FindSteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var path = key?.GetValue("SteamPath") as string;

        // Steam writes this value with forward slashes, which Path.Combine will
        // happily mix with backslashes into something Windows still opens but
        // nobody wants to read in a log.
        return string.IsNullOrWhiteSpace(path) ? null : path.Replace('/', '\\');
    }
}
```

Create `SteamAchievements.Windows/DpapiSecretStore.cs`:

```csharp
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SteamAchievements.Core.Abstractions;

namespace SteamAchievements.Windows;

/// <summary>
/// The API key, encrypted with DPAPI in the <c>CurrentUser</c> scope. That scope
/// is what makes two Windows users on one machine independent without any code
/// saying so.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _path;

    public DpapiSecretStore(string path) => _path = path;

    public string? Read()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(_path), optionalEntropy: null, DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException)
        {
            // A CurrentUser blob is unreadable from another Windows profile and
            // after a reinstall. That is the same state as "no key was ever
            // stored", and onboarding already handles it — there is no second
            // branch worth writing.
            return null;
        }
    }

    public void Write(string secret)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        File.WriteAllBytes(_path, ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret), optionalEntropy: null, DataProtectionScope.CurrentUser));
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
```

Create `SteamAchievements.Windows/ShellLinks.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Windows;

public sealed class ShellLinks : IExternalLinks
{
    public const string ApiKeyPage = "https://steamcommunity.com/dev/apikey";

    private readonly string _dataFolder;

    public ShellLinks(string dataFolder) => _dataFolder = dataFolder;

    public void OpenApiKeyPage() => OpenUrl(ApiKeyPage);

    public void OpenDataFolder() => OpenUrl(_dataFolder);

    public void OpenUrl(string url)
    {
        try
        {
            // UseShellExecute defaults to false on .NET, where a URL is treated
            // as an executable path and throws Win32Exception.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or ObjectDisposedException)
        {
            // No browser, no file association, or a folder that no longer
            // exists. Nothing useful to do, and nothing worth taking the window
            // down for.
        }
    }
}
```

Create `SteamAchievements.Windows/WebView2Probe.cs`:

```csharp
using Microsoft.Web.WebView2.Core;

namespace SteamAchievements.Windows;

/// <summary>
/// Without the Evergreen runtime a BlazorWebView shows an empty window, which is
/// an unacceptable default answer. Asked before the view is constructed.
/// </summary>
public static class WebView2Probe
{
    public static bool IsRuntimeInstalled()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 3: Confirm Core still compiles and its tests still pass**

Run: `dotnet build SteamAchievements.Core`
Expected: Build succeeded.

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: PASS. Nothing in this task touched Core, so a failure means something was edited by accident.

- [ ] **Step 4: Commit**

```bash
git add SteamAchievements.Windows
git commit -m "feat: add the Windows implementations behind the Core abstractions"
```

---

## Task 11: The window and the composition root

**Files:**
- Create: `SteamAchievements.Windows/HostStartup.cs`
- Create: `SteamAchievements.Windows/Components/_Imports.razor`
- Create: `SteamAchievements.Windows/Components/Routes.razor`
- Create: `SteamAchievements.Windows/wwwroot/index.html`
- Modify: `SteamAchievements.Windows/App.xaml`
- Modify: `SteamAchievements.Windows/App.xaml.cs`
- Modify: `SteamAchievements.Windows/MainWindow.xaml`
- Modify: `SteamAchievements.Windows/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–10, plus `SqliteLibraryQuery`, `SqliteUserPreferences`, `GameRepository`, `Database` (pre-existing) and `QueueState`, `IClock`, `SystemClock`, `AppShell` from `SteamAchievements.UI`.
- Produces: `HostStartup(IServiceProvider? Services, string StartPath, string? FailureMessage, string DataFolder)`.

- [ ] **Step 1: Write the host page**

Create `SteamAchievements.Windows/wwwroot/index.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Steam Achievements Tracker</title>
    <base href="/" />
    <link rel="stylesheet" href="_content/SteamAchievements.UI/app.css" />
    <!--
        The bundle of every component's isolated CSS is named after the HOST
        assembly, not after the class library. Preview links
        SteamAchievements.Preview.styles.css; getting this wrong here produces a
        fully working but completely unstyled application.
    -->
    <link rel="stylesheet" href="SteamAchievements.Windows.styles.css" />
</head>
<body>
    <div id="app">Loading…</div>

    <div id="blazor-error-ui">
        An unhandled error has occurred.
        <a href="" class="reload">Reload</a>
    </div>

    <script src="_framework/blazor.webview.js"></script>
    <script src="_content/SteamAchievements.UI/queue-scroll.js"></script>
</body>
</html>
```

- [ ] **Step 2: Write the router**

Create `SteamAchievements.Windows/Components/_Imports.razor`:

```razor
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using SteamAchievements.UI.Layout
```

Create `SteamAchievements.Windows/Components/Routes.razor`:

```razor
@* Routable components live in the class library, so the router has to be told
   to look there as well as in this assembly. Not shared with Preview: that host
   is server-rendered and its Routes.razor sits inside a different pipeline. *@
<Router AppAssembly="typeof(Routes).Assembly"
        AdditionalAssemblies="new[] { typeof(AppShell).Assembly }">
    <Found Context="routeData">
        <RouteView RouteData="routeData" DefaultLayout="typeof(AppShell)" />
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

- [ ] **Step 3: Write the startup record**

Create `SteamAchievements.Windows/HostStartup.cs`:

```csharp
namespace SteamAchievements.Windows;

/// <summary>
/// What the window needs to know before it decides what to draw.
/// <paramref name="FailureMessage"/> is non-null when composition itself failed
/// — a locked or corrupt database — in which case there is no service provider
/// and the window shows the message instead of a WebView.
/// </summary>
public sealed record HostStartup(
    IServiceProvider? Services,
    string StartPath,
    string? FailureMessage,
    string DataFolder);
```

- [ ] **Step 4: Write the window**

Replace `SteamAchievements.Windows/MainWindow.xaml`:

```xml
<Window x:Class="SteamAchievements.Windows.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Steam Achievements Tracker"
        Height="720" Width="1100" MinHeight="600" MinWidth="900"
        Background="#12141a">
    <!-- The dark window background matters: during a resize WPF paints the
         window before the WebView catches up, and the default is white. -->
    <Grid x:Name="Root">
        <StackPanel x:Name="Placeholder" HorizontalAlignment="Center" VerticalAlignment="Center">
            <TextBlock Text="Steam Achievements Tracker" Foreground="#e8e6e3"
                       FontSize="20" HorizontalAlignment="Center" />
            <TextBlock x:Name="PlaceholderMessage" Text="Starting…" Foreground="#8b8b8b"
                       FontSize="13" Margin="0,10,0,0" MaxWidth="520"
                       TextWrapping="Wrap" TextAlignment="Center" />
            <Button x:Name="PlaceholderAction" Visibility="Collapsed" Margin="0,18,0,0"
                    Padding="14,6" HorizontalAlignment="Center" Click="OnPlaceholderAction" />
        </StackPanel>
    </Grid>
</Window>
```

Replace `SteamAchievements.Windows/MainWindow.xaml.cs`:

```csharp
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Windows;

public partial class MainWindow : Window
{
    private const string WebView2Download = "https://developer.microsoft.com/microsoft-edge/webview2/";

    private readonly HostStartup _startup;
    private Action? _placeholderAction;

    public MainWindow(HostStartup startup)
    {
        _startup = startup;
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (_startup.FailureMessage is not null || _startup.Services is null)
        {
            ShowMessage(
                $"The application could not open its database.\n\n{_startup.FailureMessage}\n\nData folder: {_startup.DataFolder}",
                "Open data folder",
                () => new ShellLinks(_startup.DataFolder).OpenDataFolder());
            return;
        }

        if (!WebView2Probe.IsRuntimeInstalled())
        {
            ShowMessage(
                "This application needs the Microsoft Edge WebView2 runtime, which is not installed on this machine.",
                "Install WebView2",
                () => _startup.Services.GetRequiredService<IExternalLinks>().OpenUrl(WebView2Download));
            return;
        }

        var view = new BlazorWebView
        {
            HostPage = "wwwroot/index.html",
            Services = _startup.Services,
            StartPath = _startup.StartPath,
        };

        view.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.Routes),
        });

        // WebView2 takes hundreds of milliseconds to come up. The placeholder
        // covers exactly that gap.
        view.BlazorWebViewInitialized += (_, _) => Placeholder.Visibility = Visibility.Collapsed;

        Root.Children.Add(view);
    }

    private void ShowMessage(string message, string actionLabel, Action action)
    {
        PlaceholderMessage.Text = message;
        PlaceholderAction.Content = actionLabel;
        PlaceholderAction.Visibility = Visibility.Visible;
        _placeholderAction = action;
    }

    private void OnPlaceholderAction(object sender, RoutedEventArgs e) => _placeholderAction?.Invoke();
}
```

- [ ] **Step 5: Write the composition root**

Replace `SteamAchievements.Windows/App.xaml`:

```xml
<Application x:Class="SteamAchievements.Windows.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- No StartupUri: the window is constructed in OnStartup, because it needs
         a composed service provider and a start path. -->
    <Application.Resources />
</Application>
```

Replace `SteamAchievements.Windows/App.xaml.cs`:

```csharp
using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.App;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Sync;
using SteamAchievements.UI.State;

namespace SteamAchievements.Windows;

public partial class App : Application
{
    private ServiceProvider? _services;
    private readonly List<SqliteConnection> _connections = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = DataPaths.Resolve(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        HostStartup startup;

        try
        {
            paths.EnsureFolderExists();
            startup = Compose(paths);
        }
        catch (Exception failure) when (
            failure is SqliteException or IOException or UnauthorizedAccessException)
        {
            startup = new HostStartup(null, "/", failure.Message, paths.Folder);
        }

        MainWindow = new MainWindow(startup);
        MainWindow.Show();
    }

    private HostStartup Compose(DataPaths paths)
    {
        // The order of these three is not free. Database.OpenRead deliberately
        // does not migrate — schema ownership belongs to the writer — so opening
        // the reader first makes GetSummary fail on a missing settings table on
        // a clean machine. Do not reorder.
        var writer = Track(Database.Open(paths.DatabaseFile));
        var reader = Track(Database.OpenRead(paths.DatabaseFile));
        var settings = Track(Database.Open(paths.DatabaseFile));

        var secrets = new DpapiSecretStore(paths.SecretFile);
        var accounts = new SqliteAccountStore(settings);
        var journal = new SyncJournal(settings);
        var locator = new SteamAccountLocator(new RegistrySteamPathProvider());
        var community = new SteamCommunityClient(
            new HttpClient { BaseAddress = new Uri("https://steamcommunity.com/") });

        var services = new ServiceCollection();

        // One window, one session: everything the preview host registers as
        // Scoped is a singleton here.
        services.AddSingleton<ISecretStore>(secrets);
        services.AddSingleton<IAccountStore>(accounts);
        services.AddSingleton(journal);
        services.AddSingleton(locator);
        services.AddSingleton(community);
        services.AddSingleton<IExternalLinks>(new ShellLinks(paths.Folder));

        services.AddSingleton<ILibraryQuery>(new SqliteLibraryQuery(reader));
        services.AddSingleton<IUserPreferences>(new SqliteUserPreferences(settings));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<QueueState>();

        // A new client per key rather than one for the process: the user can
        // replace the key in settings, and the next sync has to use the new one.
        services.AddSingleton<Func<string, SteamApiClient>>(_ => key =>
            new SteamApiClient(
                new HttpClient { BaseAddress = new Uri("https://api.steampowered.com/") }, key));

        services.AddSingleton(new GameRepository(writer));
        services.AddSingleton<ISyncRunner, LiveSyncRunner>();
        services.AddSingleton<Func<DateTimeOffset>>(_ => () => DateTimeOffset.UtcNow);
        services.AddSingleton<SyncCoordinator>();
        services.AddSingleton<ISyncPresenter>(s => s.GetRequiredService<SyncCoordinator>());
        services.AddSingleton<ISyncController>(s => s.GetRequiredService<SyncCoordinator>());

        services.AddSingleton<IOnboarding, OnboardingService>();
        services.AddSingleton<IAccountAdmin>(_ => new AccountAdminService(
            settings, accounts, secrets, locator, community));

        _services = services.BuildServiceProvider();

        var step = OnboardingState.Evaluate(accounts.Current?.SteamId64, !string.IsNullOrEmpty(secrets.Read()));

        return new HostStartup(
            _services,
            step == OnboardingStep.Ready ? "/" : "/onboarding",
            null,
            paths.Folder);
    }

    private SqliteConnection Track(SqliteConnection connection)
    {
        _connections.Add(connection);
        return connection;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Order matters as much as it did on the way in. Disposing the
        // coordinator cancels the sync and waits for it; closing the connections
        // first would leave the orchestrator's worker pool writing into disposed
        // handles.
        _services?.GetService<SyncCoordinator>()?.Dispose();
        _services?.Dispose();

        foreach (var connection in _connections)
        {
            connection.Dispose();
        }

        base.OnExit(e);
    }
}
```

- [ ] **Step 6: Confirm Core is untouched**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add SteamAchievements.Windows
git commit -m "feat: host the Blazor components in a WPF window"
```

---

## Task 12: CI, documentation, and the first Windows verification

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `README.md`
- Modify: `CLAUDE.md`
- Modify: `docs/superpowers/specs/2026-07-26-windows-host-design.md`

- [ ] **Step 1: Make the single-file check actually check**

In `.github/workflows/ci.yml`, in the `Verify the publish output really is a single file` step, replace:

```powershell
          $files = Get-ChildItem publish -File
```

with:

```powershell
          # -Recurse is load-bearing. Without it this step cannot see
          # subdirectories, and both the WebView2 loader and the BlazorWebView
          # static assets publish into subdirectories — so it would pass green
          # on an artifact that is not a single file at all.
          $files = Get-ChildItem publish -File -Recurse
```

- [ ] **Step 2: Say what to do when WebView2 is missing**

In `README.md`, add a section before the licence:

```markdown
## Requirements

Windows 10 or 11, and the Microsoft Edge WebView2 runtime.

WebView2 ships with Microsoft Edge and is present on almost every up-to-date
Windows installation. If it is missing, the application says so on startup and
links to the installer rather than showing an empty window. It can also be
installed ahead of time from
<https://developer.microsoft.com/microsoft-edge/webview2/>.

The application is distributed as a single unsigned `.exe`. SmartScreen will
warn about it on first run; that is expected, and the reasoning is in the design
document under "Explicitly out of scope".
```

- [ ] **Step 3: Update the project memory**

In `CLAUDE.md`, replace the whole `## Current state` section with:

```markdown
## Current state

`SteamAchievements.Core` holds all the logic and is covered by unit tests: VDF
parsing, the logged-in account, the Steam API client and its error taxonomy, the
public profile endpoint, SQLite storage, the sync planner and orchestrator, the
sync state machine, onboarding, account administration, and the ranking formula.

`SteamAchievements.UI` holds the Blazor components. `SteamAchievements.Preview`
is a development-only host that renders them from macOS against fixtures.
`SteamAchievements.Windows` is the real host: a WPF window with a
`BlazorWebView`, plus the four Windows-only classes — registry, DPAPI, shell,
WebView2 probe.

Data lives in `%LOCALAPPDATA%\SteamAchievementsTracker\`: `library.db` and
`apikey.bin`.
```

Then add to the `## Facts learned the hard way` section:

```markdown
- **The publish check needs `-Recurse`.** `Get-ChildItem publish -File` does not
  see subdirectories, and both WebView2's loader and BlazorWebView's static
  assets publish into them. Without `-Recurse` the check passes green on an
  artifact that is not a single file.
- **`Microsoft.Web.WebView2` copies its loader twice.** Its `Common.targets`
  adds `WebView2Loader.dll` as a `Content` item linked into
  `runtimes\win-x64\native\`, separately from the RID asset `PublishSingleFile`
  bundles. `WebView2NeverCopyLoaderDllToOutputDirectory` turns the extra copy
  off.
- **The isolated-CSS bundle is named after the host assembly.** The WPF host
  links `SteamAchievements.Windows.styles.css`; Preview links
  `SteamAchievements.Preview.styles.css`. Getting it wrong gives a fully working
  but completely unstyled application.
- **DPAPI `CurrentUser` blobs are unreadable from another Windows profile.**
  `Read` catches `CryptographicException` and returns null, which is the same
  state as "no key stored". That is deliberate, not a swallowed error.
```

- [ ] **Step 4: Record where the plan diverged**

Append to `docs/superpowers/specs/2026-07-26-windows-host-design.md`:

```markdown
## 12. Divergences from this spec during implementation

[One bullet per place where the shipped code differs from what sections 1-11
described, and why. If nothing diverged, say so explicitly — an empty section is
a claim, and a claim is what the next reader needs.]
```

Fill it in from what actually happened. These are already known before execution starts and must appear in the list:

- `LiveSyncRunner` was added to `Core/Sync`; §5.2 described only a "five-line adapter over the real orchestrator". It turned out to need `ISecretStore`, because the key can be replaced while the application runs and a `SyncOrchestrator` built once at startup would keep using the old one.
- `SyncCoordinator` uses a private `InlineProgress<T>` rather than `System.Progress<T>`. `Progress<T>` captures a SynchronizationContext and posts asynchronously, which would let the recorded `games_synced` lag behind the run it belongs to.
- `SyncCoordinator.Completion` is public. §5.2 did not mention it; the composition root needs it to await an in-flight sync before disposing the SQLite connections.
- `SyncJournal.MarkSyncCompleted` writes `settings.last_full_sync_at` after every successful run, not only after a full one. The column name predates the distinction and the sidebar reads it as "last sync".
- The assembly is still named `SteamAchievements.Windows`, so the published artifact is `SteamAchievements.Windows.exe`. Renaming it would be nicer for a download but changes the isolated-CSS bundle name in `index.html` too; it is a cosmetic change and was left for its own commit.

- [ ] **Step 5: Confirm the whole local suite passes**

Run: `dotnet test SteamAchievements.Core.Tests`
Expected: PASS.

Run: `dotnet build SteamAchievements.Preview`
Expected: Build succeeded. The preview host must keep compiling — `SyncRunView` changed shape in Task 4.

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/ci.yml README.md CLAUDE.md \
        docs/superpowers/specs/2026-07-26-windows-host-design.md
git commit -m "docs: record the Windows host and fix the publish check"
```

- [ ] **Step 7: Push and verify on Windows in one pass**

Push the branch and wait for CI. Then download the artifact and check all seven items from §9.1 of the spec **in a single session** — each round trip is three to five minutes, and they are all independent:

1. `publish/` contains exactly one file, under the recursive check (CI answers this one).
2. The application starts and draws the queue rather than an empty window.
3. Static assets arrived: fonts render, the layout is styled, keyboard scrolling in the queue works.
4. The registry is read and the signed-in Steam account is offered during onboarding.
5. A key pasted during onboarding survives a restart.
6. The key page opens in a browser.
7. The placeholder gives way to the WebView rather than staying up.

Record what failed, if anything, in the divergences section rather than fixing it silently.

---

## Done

The application runs. What remains before it is finished is on the UI branch, not this one: the five screens that do not exist yet, and the four contract changes §10.1 of the spec asks of them.
