# Core Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Take the repository from empty to a working, fully tested core that reads the local Steam install, syncs a library from the Steam Web API into SQLite, and ranks games by remaining completion effort.

**Architecture:** All logic lives in `SteamAchievements.Core`, which targets plain `net10.0` and never touches a Windows API. Windows-specific behaviour is reached only through interfaces in `Core/Abstractions`, implemented later in the WPF host. Tests replay recorded HTTP fixtures through a substituted `HttpMessageHandler`, so the entire plan is verifiable on macOS with `dotnet test`.

**Tech Stack:** .NET 10, xUnit, Microsoft.Data.Sqlite, Dapper, Polly, System.Text.Json.

**Scope note:** This plan deliberately excludes all UI, the WPF host beyond an empty window, onboarding flows and the clipboard watcher. Those belong to the follow-up plan, because they can only be verified through CI on a Windows machine.

## Global Constraints

- Target framework: `net10.0` for every project except `SteamAchievements.Windows`, which is `net10.0-windows`.
- Everything committed is in **English** — code, comments, test names, commit messages.
- `SteamAchievements.Core` must never reference `Microsoft.Win32`, `System.Security.Cryptography.ProtectedData`, or any WPF assembly. Violating this breaks local development on macOS.
- Tests never perform network I/O. All HTTP responses come from fixtures under `testdata/`.
- Committed fixtures must be anonymized: no real `key=` values, `steamid` replaced with `76561190000000000`.
- Steam API behaviour is documented in `docs/steam-api.md`. Do not infer endpoint behaviour from memory; that file records what was verified against the live API.
- Commit after every task. Use Conventional Commits (`feat:`, `test:`, `chore:`).

## File Structure

```
SteamAchievements.sln

SteamAchievements.Core/
├─ Abstractions/ISteamPathProvider.cs      contract for locating Steam (registry on Windows)
├─ Abstractions/ISecretStore.cs            contract for storing the API key (DPAPI on Windows)
├─ Local/VdfParser.cs                      Valve KeyValues text format → tree
├─ Local/LoginUsersReader.cs               loginusers.vdf → SteamAccount list
├─ Steam/SteamApiErrorKind.cs              error taxonomy
├─ Steam/SteamApiException.cs              typed failure with kind + status
├─ Steam/SteamApiClient.cs                 typed HttpClient, one method per endpoint
├─ Steam/Dtos.cs                           wire DTOs for deserialization
├─ Data/Database.cs                        connection factory + migrations
├─ Data/Models.cs                          domain records persisted to SQLite
├─ Data/GameRepository.cs                  reads/writes games, achievements, progress
├─ Sync/SyncPlanner.cs                     pure: current state → work items
├─ Sync/SyncOrchestrator.cs                executes work items with limits and retries
├─ Sync/SyncProgress.cs                    progress report record
└─ Analytics/EffortCalculator.cs           pure: rarity → cost → game ranking

SteamAchievements.Core.Tests/
├─ Local/VdfParserTests.cs
├─ Local/LoginUsersReaderTests.cs
├─ Steam/SteamApiClientTests.cs
├─ Steam/FakeHttpMessageHandler.cs         test helper
├─ Data/GameRepositoryTests.cs
├─ Sync/SyncPlannerTests.cs
└─ Analytics/EffortCalculatorTests.cs

testdata/
├─ loginusers.vdf
├─ owned_games.json
├─ schema_for_game.json
├─ player_achievements.json
├─ global_percentages.json
└─ error_unauthorized.html

.github/workflows/ci.yml
```

---

### Task 1: Solution skeleton and CI pipeline

Deliberately first and deliberately end-to-end: the riskiest part of this setup is the Windows build, and discovering it is broken after ten tasks of logic is far worse than discovering it now on an empty WPF window.

**Files:**
- Create: `SteamAchievements.sln`, four projects, `.github/workflows/ci.yml`
- Test: `SteamAchievements.Core.Tests/SmokeTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces: the solution layout every later task builds on

- [ ] **Step 1: Create the solution and projects**

```bash
dotnet new sln -n SteamAchievements
dotnet new classlib     -n SteamAchievements.Core       -f net10.0
dotnet new xunit        -n SteamAchievements.Core.Tests -f net10.0
dotnet new razorclasslib -n SteamAchievements.UI        -f net10.0
dotnet new wpf          -n SteamAchievements.Windows    -f net10.0-windows

dotnet sln add SteamAchievements.Core SteamAchievements.Core.Tests \
               SteamAchievements.UI SteamAchievements.Windows
dotnet add SteamAchievements.Core.Tests reference SteamAchievements.Core
dotnet add SteamAchievements.Windows    reference SteamAchievements.Core SteamAchievements.UI
rm SteamAchievements.Core/Class1.cs
```

- [ ] **Step 2: Write the smoke test**

`SteamAchievements.Core.Tests/SmokeTests.cs`:

```csharp
namespace SteamAchievements.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void TestProjectReferencesCore()
    {
        var assembly = typeof(Local.VdfParser).Assembly;
        Assert.Equal("SteamAchievements.Core", assembly.GetName().Name);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test`
Expected: FAIL — `VdfParser` does not exist yet.

- [ ] **Step 4: Add the placeholder type so the reference resolves**

`SteamAchievements.Core/Local/VdfParser.cs`:

```csharp
namespace SteamAchievements.Core.Local;

public static class VdfParser
{
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test`
Expected: PASS, 1 test.

- [ ] **Step 6: Write the CI workflow**

`.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main]
    tags: ['v*']
  pull_request:
  workflow_dispatch:

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet test SteamAchievements.Core.Tests --configuration Release --logger trx

  build-windows:
    runs-on: windows-latest
    needs: test
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Publish self-contained single file
        run: >
          dotnet publish SteamAchievements.Windows
          --configuration Release
          --runtime win-x64
          --self-contained true
          -p:PublishSingleFile=true
          -p:PublishTrimmed=false
          --output publish
      - uses: actions/upload-artifact@v4
        with:
          name: SteamAchievementsTracker-win-x64
          path: publish/
```

Trimming stays off deliberately: WPF and Blazor use reflection, and a trimmed build fails at runtime in ways only reproducible on Windows.

- [ ] **Step 7: Commit and verify the pipeline end to end**

```bash
git add -A
git commit -m "chore: scaffold solution and CI pipeline"
git push
```

Then confirm on GitHub that both jobs pass and that the artifact downloads and launches (an empty window) on the Windows machine. **Do not start Task 2 until the artifact has been launched successfully** — this is the one moment where a broken Windows build is cheap to fix.

---

### Task 2: VDF parser

Valve's KeyValues format is what `loginusers.vdf`, `libraryfolders.vdf` and every `appmanifest_*.acf` use. It is plain text, so it parses and tests entirely on macOS.

**Files:**
- Create: `SteamAchievements.Core/Local/VdfParser.cs` (replacing the placeholder), `SteamAchievements.Core/Local/VdfNode.cs`
- Test: `SteamAchievements.Core.Tests/Local/VdfParserTests.cs`
- Create: `testdata/loginusers.vdf`

**Interfaces:**
- Consumes: nothing
- Produces: `VdfNode` with `IReadOnlyDictionary<string, VdfNode> Children`, `string? Value`, indexer `this[string key]`; `VdfParser.Parse(string text) → VdfNode`

- [ ] **Step 1: Create the fixture**

`testdata/loginusers.vdf` — synthetic but structurally identical to the real file. Replace it with a real anonymized capture from the Windows machine when available.

```
"users"
{
	"76561190000000001"
	{
		"AccountName"		"olduser"
		"PersonaName"		"Old User"
		"RememberPassword"		"1"
		"WantsOfflineMode"		"0"
		"MostRecent"		"0"
		"Timestamp"		"1690000000"
	}
	"76561190000000002"
	{
		"AccountName"		"currentuser"
		"PersonaName"		"Current \"Quoted\" User"
		"RememberPassword"		"1"
		"WantsOfflineMode"		"0"
		"MostRecent"		"1"
		"Timestamp"		"1750000000"
	}
}
```

- [ ] **Step 2: Write the failing tests**

`SteamAchievements.Core.Tests/Local/VdfParserTests.cs`:

```csharp
using SteamAchievements.Core.Local;

namespace SteamAchievements.Core.Tests.Local;

public class VdfParserTests
{
    [Fact]
    public void ParsesNestedSections()
    {
        var node = VdfParser.Parse("""
            "users"
            {
                "123"
                {
                    "AccountName"		"someone"
                }
            }
            """);

        Assert.Equal("someone", node["users"]["123"]["AccountName"].Value);
    }

    [Fact]
    public void ParsesEscapedQuotesInsideValues()
    {
        var node = VdfParser.Parse("""
            "root"
            {
                "PersonaName"		"Current \"Quoted\" User"
            }
            """);

        Assert.Equal("Current \"Quoted\" User", node["root"]["PersonaName"].Value);
    }

    [Fact]
    public void IgnoresCommentLines()
    {
        var node = VdfParser.Parse("""
            // leading comment
            "root"
            {
                "key"		"value"   // trailing comment
            }
            """);

        Assert.Equal("value", node["root"]["key"].Value);
    }

    [Fact]
    public void ReturnsEmptyNodeForMissingKey()
    {
        var node = VdfParser.Parse("\"root\"\n{\n}\n");

        Assert.Null(node["root"]["absent"].Value);
        Assert.Empty(node["root"]["absent"].Children);
    }

    [Fact]
    public void ThrowsOnUnbalancedBraces()
    {
        Assert.Throws<FormatException>(() => VdfParser.Parse("\"root\"\n{\n"));
    }
}
```

The missing-key case returns an empty node rather than throwing, because callers chain several lookups and a null check at every level would drown the reading code.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter VdfParserTests`
Expected: FAIL — `Parse` and `VdfNode` do not exist.

- [ ] **Step 4: Implement `VdfNode`**

`SteamAchievements.Core/Local/VdfNode.cs`:

```csharp
namespace SteamAchievements.Core.Local;

public sealed class VdfNode
{
    private static readonly VdfNode Empty = new();

    private readonly Dictionary<string, VdfNode> _children = new(StringComparer.OrdinalIgnoreCase);

    public string? Value { get; init; }

    public IReadOnlyDictionary<string, VdfNode> Children => _children;

    public VdfNode this[string key] =>
        _children.TryGetValue(key, out var child) ? child : Empty;

    internal void Add(string key, VdfNode child) => _children[key] = child;
}
```

- [ ] **Step 5: Implement the parser**

`SteamAchievements.Core/Local/VdfParser.cs`:

```csharp
namespace SteamAchievements.Core.Local;

/// <summary>
/// Parser for Valve's KeyValues text format used by loginusers.vdf,
/// libraryfolders.vdf and appmanifest_*.acf.
/// </summary>
public static class VdfParser
{
    public static VdfNode Parse(string text)
    {
        var position = 0;
        var root = new VdfNode();

        while (true)
        {
            var key = ReadToken(text, ref position);
            if (key is null)
            {
                return root;
            }

            root.Add(key, ReadValue(text, ref position, key));
        }
    }

    private static VdfNode ReadValue(string text, ref int position, string key)
    {
        SkipTrivia(text, ref position);

        if (position < text.Length && text[position] == '{')
        {
            position++;
            var section = new VdfNode();

            while (true)
            {
                SkipTrivia(text, ref position);

                if (position >= text.Length)
                {
                    throw new FormatException($"Unbalanced braces in section '{key}'.");
                }

                if (text[position] == '}')
                {
                    position++;
                    return section;
                }

                var childKey = ReadToken(text, ref position)
                    ?? throw new FormatException($"Unbalanced braces in section '{key}'.");

                section.Add(childKey, ReadValue(text, ref position, childKey));
            }
        }

        var scalar = ReadToken(text, ref position)
            ?? throw new FormatException($"Key '{key}' has no value.");

        return new VdfNode { Value = scalar };
    }

    private static string? ReadToken(string text, ref int position)
    {
        SkipTrivia(text, ref position);

        if (position >= text.Length || text[position] != '"')
        {
            return null;
        }

        position++;
        var builder = new System.Text.StringBuilder();

        while (position < text.Length && text[position] != '"')
        {
            if (text[position] == '\\' && position + 1 < text.Length)
            {
                position++;
                builder.Append(text[position] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    var other => other,
                });
            }
            else
            {
                builder.Append(text[position]);
            }

            position++;
        }

        position++;
        return builder.ToString();
    }

    private static void SkipTrivia(string text, ref int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
            }
            else if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                while (position < text.Length && text[position] != '\n')
                {
                    position++;
                }
            }
            else
            {
                return;
            }
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter VdfParserTests`
Expected: PASS, 5 tests.

- [ ] **Step 7: Commit**

```bash
git add SteamAchievements.Core/Local SteamAchievements.Core.Tests/Local testdata/loginusers.vdf
git commit -m "feat: add Valve KeyValues (VDF) parser"
```

---

### Task 3: Reading the logged-in Steam account

**Files:**
- Create: `SteamAchievements.Core/Local/LoginUsersReader.cs`, `SteamAchievements.Core/Local/SteamAccount.cs`, `SteamAchievements.Core/Abstractions/ISteamPathProvider.cs`
- Test: `SteamAchievements.Core.Tests/Local/LoginUsersReaderTests.cs`

**Interfaces:**
- Consumes: `VdfParser.Parse`
- Produces: `record SteamAccount(ulong SteamId64, string AccountName, string PersonaName, bool MostRecent, DateTimeOffset Timestamp)`; `LoginUsersReader.Read(string vdfText) → IReadOnlyList<SteamAccount>`; `LoginUsersReader.SelectActive(IReadOnlyList<SteamAccount>) → SteamAccount?`; `interface ISteamPathProvider { string? FindSteamPath(); }`

- [ ] **Step 1: Write the failing tests**

`SteamAchievements.Core.Tests/Local/LoginUsersReaderTests.cs`:

```csharp
using SteamAchievements.Core.Local;

namespace SteamAchievements.Core.Tests.Local;

public class LoginUsersReaderTests
{
    private static string Fixture() => File.ReadAllText(TestPaths.Data("loginusers.vdf"));

    [Fact]
    public void ReadsAllAccounts()
    {
        var accounts = LoginUsersReader.Read(Fixture());

        Assert.Equal(2, accounts.Count);
        Assert.Contains(accounts, a => a.SteamId64 == 76561190000000002 && a.AccountName == "currentuser");
    }

    [Fact]
    public void SelectsAccountFlaggedMostRecent()
    {
        var active = LoginUsersReader.SelectActive(LoginUsersReader.Read(Fixture()));

        Assert.NotNull(active);
        Assert.Equal(76561190000000002u, active!.SteamId64);
    }

    [Fact]
    public void FallsBackToNewestTimestampWhenNoMostRecentFlag()
    {
        var accounts = LoginUsersReader.Read("""
            "users"
            {
                "76561190000000001"
                {
                    "AccountName"		"a"
                    "PersonaName"		"A"
                    "MostRecent"		"0"
                    "Timestamp"		"1000"
                }
                "76561190000000002"
                {
                    "AccountName"		"b"
                    "PersonaName"		"B"
                    "MostRecent"		"0"
                    "Timestamp"		"2000"
                }
            }
            """);

        Assert.Equal(76561190000000002u, LoginUsersReader.SelectActive(accounts)!.SteamId64);
    }

    [Fact]
    public void ReturnsNullWhenFileHasNoUsers()
    {
        Assert.Null(LoginUsersReader.SelectActive(LoginUsersReader.Read("\"users\"\n{\n}\n")));
    }

    [Fact]
    public void SkipsEntriesWithUnparseableSteamId()
    {
        var accounts = LoginUsersReader.Read("""
            "users"
            {
                "not-a-number"
                {
                    "AccountName"		"broken"
                }
            }
            """);

        Assert.Empty(accounts);
    }
}
```

- [ ] **Step 2: Add the test path helper**

`SteamAchievements.Core.Tests/TestPaths.cs`:

```csharp
namespace SteamAchievements.Core.Tests;

public static class TestPaths
{
    public static string Data(string fileName)
    {
        var directory = AppContext.BaseDirectory;

        while (directory is not null && !Directory.Exists(Path.Combine(directory, "testdata")))
        {
            directory = Directory.GetParent(directory)?.FullName;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Could not locate the testdata directory.");
        }

        return Path.Combine(directory, "testdata", fileName);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter LoginUsersReaderTests`
Expected: FAIL — `LoginUsersReader` does not exist.

- [ ] **Step 4: Implement the account record and the path contract**

`SteamAchievements.Core/Local/SteamAccount.cs`:

```csharp
namespace SteamAchievements.Core.Local;

public sealed record SteamAccount(
    ulong SteamId64,
    string AccountName,
    string PersonaName,
    bool MostRecent,
    DateTimeOffset Timestamp);
```

`SteamAchievements.Core/Abstractions/ISteamPathProvider.cs`:

```csharp
namespace SteamAchievements.Core.Abstractions;

/// <summary>
/// Locates the local Steam installation. Implemented on Windows by reading
/// HKCU\Software\Valve\Steam\SteamPath; kept behind an interface so Core
/// stays free of Windows APIs and testable on any platform.
/// </summary>
public interface ISteamPathProvider
{
    string? FindSteamPath();
}
```

- [ ] **Step 5: Implement the reader**

`SteamAchievements.Core/Local/LoginUsersReader.cs`:

```csharp
namespace SteamAchievements.Core.Local;

public static class LoginUsersReader
{
    public static IReadOnlyList<SteamAccount> Read(string vdfText)
    {
        var users = VdfParser.Parse(vdfText)["users"];
        var accounts = new List<SteamAccount>();

        foreach (var (rawId, node) in users.Children)
        {
            if (!ulong.TryParse(rawId, out var steamId))
            {
                continue;
            }

            _ = long.TryParse(node["Timestamp"].Value, out var timestamp);

            accounts.Add(new SteamAccount(
                steamId,
                node["AccountName"].Value ?? string.Empty,
                node["PersonaName"].Value ?? string.Empty,
                node["MostRecent"].Value == "1",
                DateTimeOffset.FromUnixTimeSeconds(timestamp)));
        }

        return accounts;
    }

    public static SteamAccount? SelectActive(IReadOnlyList<SteamAccount> accounts) =>
        accounts.FirstOrDefault(a => a.MostRecent)
        ?? accounts.MaxBy(a => a.Timestamp);
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter LoginUsersReaderTests`
Expected: PASS, 5 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: read the active Steam account from loginusers.vdf"
```

---

### Task 4: Steam API error classification

This is the task that pays for itself. Steam returns 400 or 401 for a missing key depending on the endpoint, returns 400 as a *success-shaped* answer for games without achievements, and sends error bodies as HTML rather than JSON. Getting this wrong surfaces as `JsonException` instead of "your key is invalid".

**Files:**
- Create: `SteamAchievements.Core/Steam/SteamApiErrorKind.cs`, `SteamAchievements.Core/Steam/SteamApiException.cs`, `SteamAchievements.Core/Steam/SteamApiClient.cs`
- Test: `SteamAchievements.Core.Tests/Steam/SteamApiClientTests.cs`, `SteamAchievements.Core.Tests/Steam/FakeHttpMessageHandler.cs`
- Create: `testdata/error_unauthorized.html`

**Interfaces:**
- Consumes: nothing
- Produces: `enum SteamApiErrorKind { InvalidKey, NoStatsForApp, RateLimited, ServerError, BadRequest, Unknown }`; `SteamApiException(SteamApiErrorKind Kind, int StatusCode, string Message)`; `SteamApiClient(HttpClient http, string apiKey)`

- [ ] **Step 1: Create the error fixture**

`testdata/error_unauthorized.html` — the real body Steam returns, captured on 2026-07-25:

```html
<html><head><title>Unauthorized</title></head><body><h1>Unauthorized</h1>Access is denied. Retrying will not help. Please verify your <pre>key=</pre> parameter.</body></html>
```

- [ ] **Step 2: Write the fake handler**

`SteamAchievements.Core.Tests/Steam/FakeHttpMessageHandler.cs`:

```csharp
using System.Net;

namespace SteamAchievements.Core.Tests.Steam;

public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<Uri> Requests { get; } = [];

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    public static FakeHttpMessageHandler Returning(HttpStatusCode status, string body, string contentType = "application/json") =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, contentType),
        });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(_responder(request));
    }
}
```

- [ ] **Step 3: Write the failing tests**

`SteamAchievements.Core.Tests/Steam/SteamApiClientTests.cs`:

```csharp
using System.Net;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Steam;

public class SteamApiClientTests
{
    private static SteamApiClient Client(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY");

    [Fact]
    public async Task ClassifiesHtmlUnauthorizedBodyAsInvalidKey()
    {
        var html = await File.ReadAllTextAsync(TestPaths.Data("error_unauthorized.html"));
        var client = Client(FakeHttpMessageHandler.Returning(HttpStatusCode.Unauthorized, html, "text/html"));

        var error = await Assert.ThrowsAsync<SteamApiException>(
            () => client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None));

        Assert.Equal(SteamApiErrorKind.InvalidKey, error.Kind);
    }

    [Fact]
    public async Task ClassifiesBadRequestWithoutKeyAsInvalidKey()
    {
        var client = Client(FakeHttpMessageHandler.Returning(
            HttpStatusCode.BadRequest, "<html><head><title>Bad Request</title></head></html>", "text/html"));

        var error = await Assert.ThrowsAsync<SteamApiException>(
            () => client.GetSchemaForGameAsync(292030, CancellationToken.None));

        Assert.Equal(SteamApiErrorKind.InvalidKey, error.Kind);
    }

    [Fact]
    public async Task ClassifiesNoStatsResponseAsNoStatsForApp()
    {
        var client = Client(FakeHttpMessageHandler.Returning(
            HttpStatusCode.BadRequest,
            "<html><head><title>Bad Request</title></head><body><h1>Bad Request</h1>Requested app has no stats</body></html>",
            "text/html"));

        var error = await Assert.ThrowsAsync<SteamApiException>(
            () => client.GetPlayerAchievementsAsync(76561190000000002, 220, CancellationToken.None));

        Assert.Equal(SteamApiErrorKind.NoStatsForApp, error.Kind);
    }

    [Fact]
    public async Task ClassifiesTooManyRequestsAsRateLimited()
    {
        var client = Client(FakeHttpMessageHandler.Returning(HttpStatusCode.TooManyRequests, string.Empty, "text/plain"));

        var error = await Assert.ThrowsAsync<SteamApiException>(
            () => client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None));

        Assert.Equal(SteamApiErrorKind.RateLimited, error.Kind);
    }

    [Fact]
    public async Task ClassifiesServerErrorAsServerError()
    {
        var client = Client(FakeHttpMessageHandler.Returning(HttpStatusCode.ServiceUnavailable, string.Empty, "text/plain"));

        var error = await Assert.ThrowsAsync<SteamApiException>(
            () => client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None));

        Assert.Equal(SteamApiErrorKind.ServerError, error.Kind);
    }

    [Fact]
    public async Task NeverLeaksTheApiKeyIntoExceptionMessages()
    {
        var client = Client(FakeHttpMessageHandler.Returning(HttpStatusCode.Unauthorized, "denied", "text/html"));

        var error = await Assert.ThrowsAsync<SteamApiException>(
            () => client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None));

        Assert.DoesNotContain("TESTKEY", error.Message);
        Assert.DoesNotContain("TESTKEY", error.ToString());
    }
}
```

The last test matters more than it looks: exception messages end up in logs and bug reports, and the API key is a credential.

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test --filter SteamApiClientTests`
Expected: FAIL — `SteamApiClient` does not exist.

- [ ] **Step 5: Implement the error taxonomy**

`SteamAchievements.Core/Steam/SteamApiErrorKind.cs`:

```csharp
namespace SteamAchievements.Core.Steam;

public enum SteamApiErrorKind
{
    /// <summary>Key missing, malformed or rejected. Retrying will not help.</summary>
    InvalidKey,

    /// <summary>The app has no achievements at all. Expected for 30-40% of a library.</summary>
    NoStatsForApp,

    RateLimited,
    ServerError,
    BadRequest,
    Unknown,
}
```

`SteamAchievements.Core/Steam/SteamApiException.cs`:

```csharp
namespace SteamAchievements.Core.Steam;

public sealed class SteamApiException : Exception
{
    public SteamApiException(SteamApiErrorKind kind, int statusCode, string message)
        : base(message)
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    public SteamApiErrorKind Kind { get; }

    public int StatusCode { get; }

    /// <summary>Retrying only makes sense for transient conditions.</summary>
    public bool IsTransient => Kind is SteamApiErrorKind.RateLimited or SteamApiErrorKind.ServerError;
}
```

- [ ] **Step 6: Implement the client's request pipeline**

`SteamAchievements.Core/Steam/SteamApiClient.cs` — endpoint methods are filled in by Task 5; this step delivers the shared send-and-classify path.

```csharp
using System.Net;
using System.Text.Json;

namespace SteamAchievements.Core.Steam;

public sealed class SteamApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly string _apiKey;

    public SteamApiClient(HttpClient http, string apiKey)
    {
        _http = http;
        _apiKey = apiKey;
    }

    internal async Task<T> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(path, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw Classify(response.StatusCode, body);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                ?? throw new SteamApiException(SteamApiErrorKind.Unknown, (int)response.StatusCode,
                    "Steam returned an empty document.");
        }
        catch (JsonException)
        {
            // A 200 with a non-JSON body means Steam served an error or an
            // interstitial page. Never surface a raw JsonException.
            throw new SteamApiException(SteamApiErrorKind.Unknown, (int)response.StatusCode,
                "Steam returned a non-JSON response.");
        }
    }

    private static SteamApiException Classify(HttpStatusCode status, string body)
    {
        // Bodies are HTML, not JSON — match on text, and never echo the body
        // back, because the request URL it may contain carries the API key.
        if (body.Contains("has no stats", StringComparison.OrdinalIgnoreCase))
        {
            return new SteamApiException(SteamApiErrorKind.NoStatsForApp, (int)status,
                "The requested app has no achievements.");
        }

        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest =>
                new SteamApiException(SteamApiErrorKind.InvalidKey, (int)status,
                    "Steam rejected the API key. Check it in settings."),

            HttpStatusCode.TooManyRequests =>
                new SteamApiException(SteamApiErrorKind.RateLimited, (int)status,
                    "Steam is rate limiting this key."),

            >= HttpStatusCode.InternalServerError =>
                new SteamApiException(SteamApiErrorKind.ServerError, (int)status,
                    $"Steam returned {(int)status}."),

            _ => new SteamApiException(SteamApiErrorKind.Unknown, (int)status,
                $"Unexpected response {(int)status} from Steam."),
        };
    }

    internal string Key => _apiKey;
}
```

Note that `Classify` collapses 400 and 401 into `InvalidKey` **after** checking for the no-stats marker. That ordering is the whole point: the same 400 means two completely different things depending on the body.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test --filter SteamApiClientTests`
Expected: FAIL on the four endpoint methods still missing. Add them as stubs calling `GetJsonAsync<JsonDocument>` with the paths defined in Task 5, then re-run — 6 tests PASS.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: classify Steam API errors from HTML bodies"
```

---

### Task 5: Typed Steam endpoints

**Files:**
- Create: `SteamAchievements.Core/Steam/Dtos.cs`
- Modify: `SteamAchievements.Core/Steam/SteamApiClient.cs`
- Test: `SteamAchievements.Core.Tests/Steam/SteamApiEndpointTests.cs`
- Create: `testdata/owned_games.json`, `testdata/schema_for_game.json`, `testdata/player_achievements.json`, `testdata/global_percentages.json`

**Interfaces:**
- Consumes: `SteamApiClient.GetJsonAsync<T>`, `SteamApiException`
- Produces:
  - `Task<IReadOnlyList<OwnedGame>> GetOwnedGamesAsync(ulong steamId, CancellationToken ct)`
  - `Task<IReadOnlyList<AchievementSchema>> GetSchemaForGameAsync(uint appId, CancellationToken ct)`
  - `Task<IReadOnlyList<PlayerAchievement>> GetPlayerAchievementsAsync(ulong steamId, uint appId, CancellationToken ct)`
  - `Task<IReadOnlyDictionary<string, double>> GetGlobalPercentagesAsync(uint appId, CancellationToken ct)`
  - `record OwnedGame(uint AppId, string Name, string IconHash, int PlaytimeForever, int PlaytimeTwoWeeks, DateTimeOffset? LastPlayed)`
  - `record AchievementSchema(string ApiName, string DisplayName, string Description, string IconUrl, string IconGrayUrl, bool IsHidden, int SortOrder)`
  - `record PlayerAchievement(string ApiName, bool Unlocked, DateTimeOffset? UnlockedAt)`

- [ ] **Step 1: Create the fixtures**

`testdata/owned_games.json`:

```json
{
  "response": {
    "game_count": 2,
    "games": [
      { "appid": 292030, "name": "The Witcher 3: Wild Hunt", "playtime_forever": 6420,
        "playtime_2weeks": 120, "img_icon_url": "abc123", "rtime_last_played": 1750000000 },
      { "appid": 220, "name": "Half-Life 2", "playtime_forever": 0,
        "img_icon_url": "def456", "rtime_last_played": 0 }
    ]
  }
}
```

`testdata/schema_for_game.json`:

```json
{
  "game": {
    "gameName": "The Witcher 3: Wild Hunt",
    "availableGameStats": {
      "achievements": [
        { "name": "ACH_1", "displayName": "Lilac and Gooseberries", "description": "Find Yennefer.",
          "hidden": 0, "icon": "https://cdn.example/ach1.jpg", "icongray": "https://cdn.example/ach1_gray.jpg" },
        { "name": "ACH_2", "displayName": "Passed the Trial", "description": "",
          "hidden": 1, "icon": "https://cdn.example/ach2.jpg", "icongray": "https://cdn.example/ach2_gray.jpg" }
      ]
    }
  }
}
```

`testdata/player_achievements.json`:

```json
{
  "playerstats": {
    "steamID": "76561190000000002",
    "gameName": "The Witcher 3: Wild Hunt",
    "achievements": [
      { "apiname": "ACH_1", "achieved": 1, "unlocktime": 1700000000 },
      { "apiname": "ACH_2", "achieved": 0, "unlocktime": 0 }
    ],
    "success": true
  }
}
```

`testdata/global_percentages.json`:

```json
{
  "achievementpercentages": {
    "achievements": [
      { "name": "ACH_1", "percent": 62.4000015258789 },
      { "name": "ACH_2", "percent": 0.400000005960464 }
    ]
  }
}
```

`testdata/private_profile.json` — what a private profile actually returns:

```json
{ "response": {} }
```

- [ ] **Step 2: Write the failing tests**

`SteamAchievements.Core.Tests/Steam/SteamApiEndpointTests.cs`:

```csharp
using System.Net;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Steam;

public class SteamApiEndpointTests
{
    private static async Task<SteamApiClient> ClientFor(string fixture, FakeHttpMessageHandler? capture = null)
    {
        var body = await File.ReadAllTextAsync(TestPaths.Data(fixture));
        var handler = capture ?? FakeHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        return new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY");
    }

    [Fact]
    public async Task ParsesOwnedGames()
    {
        var client = await ClientFor("owned_games.json");

        var games = await client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None);

        Assert.Equal(2, games.Count);
        Assert.Equal("The Witcher 3: Wild Hunt", games[0].Name);
        Assert.Equal(6420, games[0].PlaytimeForever);
        Assert.Equal(120, games[0].PlaytimeTwoWeeks);
        Assert.Null(games[1].LastPlayed);
    }

    [Fact]
    public async Task RequestsOwnedGamesWithAppInfoIncluded()
    {
        var body = await File.ReadAllTextAsync(TestPaths.Data("owned_games.json"));
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        var client = new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY");

        await client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None);

        var url = handler.Requests.Single().ToString();
        Assert.Contains("include_appinfo=1", url);
        Assert.Contains("include_played_free_games=1", url);
    }

    [Fact]
    public async Task ReturnsEmptyListForPrivateProfile()
    {
        var client = await ClientFor("private_profile.json");

        var games = await client.GetOwnedGamesAsync(76561190000000002, CancellationToken.None);

        Assert.Empty(games);
    }

    [Fact]
    public async Task ParsesAchievementSchemaPreservingOrder()
    {
        var client = await ClientFor("schema_for_game.json");

        var schema = await client.GetSchemaForGameAsync(292030, CancellationToken.None);

        Assert.Equal(2, schema.Count);
        Assert.Equal(0, schema[0].SortOrder);
        Assert.Equal(1, schema[1].SortOrder);
        Assert.True(schema[1].IsHidden);
        Assert.Equal(string.Empty, schema[1].Description);
    }

    [Fact]
    public async Task ParsesPlayerAchievements()
    {
        var client = await ClientFor("player_achievements.json");

        var progress = await client.GetPlayerAchievementsAsync(76561190000000002, 292030, CancellationToken.None);

        Assert.True(progress[0].Unlocked);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), progress[0].UnlockedAt);
        Assert.False(progress[1].Unlocked);
        Assert.Null(progress[1].UnlockedAt);
    }

    [Fact]
    public async Task RequestsGlobalPercentagesUsingGameIdParameter()
    {
        var body = await File.ReadAllTextAsync(TestPaths.Data("global_percentages.json"));
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        var client = new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY");

        var percentages = await client.GetGlobalPercentagesAsync(292030, CancellationToken.None);

        var url = handler.Requests.Single().ToString();
        Assert.Contains("gameid=292030", url);       // NOT appid — the only endpoint that differs
        Assert.DoesNotContain("key=", url);          // this endpoint needs no key
        Assert.Equal(62.4, percentages["ACH_1"], 1);
        Assert.Equal(0.4, percentages["ACH_2"], 1);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter SteamApiEndpointTests`
Expected: FAIL — endpoint methods return stubs.

- [ ] **Step 4: Implement the DTOs and domain records**

`SteamAchievements.Core/Steam/Dtos.cs`:

```csharp
using System.Text.Json.Serialization;

namespace SteamAchievements.Core.Steam;

public sealed record OwnedGame(
    uint AppId,
    string Name,
    string IconHash,
    int PlaytimeForever,
    int PlaytimeTwoWeeks,
    DateTimeOffset? LastPlayed);

public sealed record AchievementSchema(
    string ApiName,
    string DisplayName,
    string Description,
    string IconUrl,
    string IconGrayUrl,
    bool IsHidden,
    int SortOrder);

public sealed record PlayerAchievement(
    string ApiName,
    bool Unlocked,
    DateTimeOffset? UnlockedAt);

// Wire shapes below mirror Steam's JSON exactly and stay internal.

internal sealed class OwnedGamesEnvelope
{
    [JsonPropertyName("response")] public OwnedGamesResponse? Response { get; set; }
}

internal sealed class OwnedGamesResponse
{
    [JsonPropertyName("games")] public List<OwnedGameDto>? Games { get; set; }
}

internal sealed class OwnedGameDto
{
    [JsonPropertyName("appid")] public uint AppId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("img_icon_url")] public string? IconHash { get; set; }
    [JsonPropertyName("playtime_forever")] public int PlaytimeForever { get; set; }
    [JsonPropertyName("playtime_2weeks")] public int PlaytimeTwoWeeks { get; set; }
    [JsonPropertyName("rtime_last_played")] public long LastPlayed { get; set; }
}

internal sealed class SchemaEnvelope
{
    [JsonPropertyName("game")] public SchemaGame? Game { get; set; }
}

internal sealed class SchemaGame
{
    [JsonPropertyName("availableGameStats")] public SchemaStats? Stats { get; set; }
}

internal sealed class SchemaStats
{
    [JsonPropertyName("achievements")] public List<SchemaAchievementDto>? Achievements { get; set; }
}

internal sealed class SchemaAchievementDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("hidden")] public int Hidden { get; set; }
    [JsonPropertyName("icon")] public string? Icon { get; set; }
    [JsonPropertyName("icongray")] public string? IconGray { get; set; }
}

internal sealed class PlayerStatsEnvelope
{
    [JsonPropertyName("playerstats")] public PlayerStats? PlayerStats { get; set; }
}

internal sealed class PlayerStats
{
    [JsonPropertyName("achievements")] public List<PlayerAchievementDto>? Achievements { get; set; }
}

internal sealed class PlayerAchievementDto
{
    [JsonPropertyName("apiname")] public string? ApiName { get; set; }
    [JsonPropertyName("achieved")] public int Achieved { get; set; }
    [JsonPropertyName("unlocktime")] public long UnlockTime { get; set; }
}

internal sealed class GlobalPercentagesEnvelope
{
    [JsonPropertyName("achievementpercentages")] public GlobalPercentages? Percentages { get; set; }
}

internal sealed class GlobalPercentages
{
    [JsonPropertyName("achievements")] public List<GlobalPercentageDto>? Achievements { get; set; }
}

internal sealed class GlobalPercentageDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("percent")] public double Percent { get; set; }
}
```

- [ ] **Step 5: Implement the endpoint methods**

Add to `SteamAchievements.Core/Steam/SteamApiClient.cs`:

```csharp
    public async Task<IReadOnlyList<OwnedGame>> GetOwnedGamesAsync(ulong steamId, CancellationToken cancellationToken)
    {
        var envelope = await GetJsonAsync<OwnedGamesEnvelope>(
            $"IPlayerService/GetOwnedGames/v1/?key={_apiKey}&steamid={steamId}" +
            "&include_appinfo=1&include_played_free_games=1", cancellationToken);

        // A private profile answers 200 with an empty response object.
        return envelope.Response?.Games?
            .Select(g => new OwnedGame(
                g.AppId,
                g.Name ?? string.Empty,
                g.IconHash ?? string.Empty,
                g.PlaytimeForever,
                g.PlaytimeTwoWeeks,
                g.LastPlayed > 0 ? DateTimeOffset.FromUnixTimeSeconds(g.LastPlayed) : null))
            .ToList()
            ?? [];
    }

    public async Task<IReadOnlyList<AchievementSchema>> GetSchemaForGameAsync(uint appId, CancellationToken cancellationToken)
    {
        var envelope = await GetJsonAsync<SchemaEnvelope>(
            $"ISteamUserStats/GetSchemaForGame/v2/?key={_apiKey}&appid={appId}&l=english", cancellationToken);

        var achievements = envelope.Game?.Stats?.Achievements ?? [];

        // Schema order is stable and reflects the order achievements were added,
        // which is the raw material for future DLC grouping. Preserve it.
        return achievements
            .Select((a, index) => new AchievementSchema(
                a.Name ?? string.Empty,
                a.DisplayName ?? string.Empty,
                a.Description ?? string.Empty,
                a.Icon ?? string.Empty,
                a.IconGray ?? string.Empty,
                a.Hidden == 1,
                index))
            .ToList();
    }

    public async Task<IReadOnlyList<PlayerAchievement>> GetPlayerAchievementsAsync(
        ulong steamId, uint appId, CancellationToken cancellationToken)
    {
        var envelope = await GetJsonAsync<PlayerStatsEnvelope>(
            $"ISteamUserStats/GetPlayerAchievements/v1/?key={_apiKey}&steamid={steamId}&appid={appId}&l=english",
            cancellationToken);

        return envelope.PlayerStats?.Achievements?
            .Select(a => new PlayerAchievement(
                a.ApiName ?? string.Empty,
                a.Achieved == 1,
                a.Achieved == 1 && a.UnlockTime > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(a.UnlockTime)
                    : null))
            .ToList()
            ?? [];
    }

    /// <summary>
    /// Global achievement rarity. This endpoint takes <c>gameid</c> rather than
    /// <c>appid</c> and requires no API key — see docs/steam-api.md.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, double>> GetGlobalPercentagesAsync(
        uint appId, CancellationToken cancellationToken)
    {
        var envelope = await GetJsonAsync<GlobalPercentagesEnvelope>(
            $"ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={appId}", cancellationToken);

        return envelope.Percentages?.Achievements?
            .Where(a => a.Name is not null)
            .ToDictionary(a => a.Name!, a => a.Percent)
            ?? new Dictionary<string, double>();
    }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter SteamApi`
Expected: PASS, 12 tests across both API test classes.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add typed Steam API endpoints"
```

---

### Task 6: SQLite storage

**Files:**
- Create: `SteamAchievements.Core/Data/Database.cs`, `SteamAchievements.Core/Data/Models.cs`, `SteamAchievements.Core/Data/GameRepository.cs`
- Modify: `SteamAchievements.Core/SteamAchievements.Core.csproj` (add `Microsoft.Data.Sqlite`, `Dapper`)
- Test: `SteamAchievements.Core.Tests/Data/GameRepositoryTests.cs`

**Interfaces:**
- Consumes: `OwnedGame`, `AchievementSchema`, `PlayerAchievement` from Task 5
- Produces: `Database.Open(string path) → SqliteConnection` (migrated); `GameRepository(SqliteConnection)` with `UpsertOwnedGames`, `UpsertSchema`, `UpsertGlobalPercentages`, `UpsertPlayerAchievements`, `GetSyncStates`, `MarkSynced`, `MarkNoAchievements`, `GetGameProgress`

- [ ] **Step 1: Add the packages**

```bash
dotnet add SteamAchievements.Core package Microsoft.Data.Sqlite
dotnet add SteamAchievements.Core package Dapper
```

- [ ] **Step 2: Write the failing tests**

`SteamAchievements.Core.Tests/Data/GameRepositoryTests.cs`:

```csharp
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Tests.Data;

public class GameRepositoryTests
{
    private static GameRepository InMemory() => new(Database.Open(":memory:"));

    [Fact]
    public void MigrationCreatesAllTables()
    {
        using var connection = Database.Open(":memory:");

        var tables = Dapper.SqlMapper.Query<string>(connection,
            "SELECT name FROM sqlite_master WHERE type = 'table'").ToHashSet();

        Assert.Contains("games", tables);
        Assert.Contains("owned_games", tables);
        Assert.Contains("achievements", tables);
        Assert.Contains("global_percents", tables);
        Assert.Contains("player_achievements", tables);
        Assert.Contains("sync_state", tables);
        Assert.Contains("snapshots", tables);
        Assert.Contains("settings", tables);
    }

    [Fact]
    public void UpsertOwnedGamesIsIdempotent()
    {
        var repository = InMemory();
        var game = new OwnedGame(292030, "The Witcher 3", "abc", 100, 10, null);

        repository.UpsertOwnedGames([game]);
        repository.UpsertOwnedGames([game with { PlaytimeForever = 200 }]);

        var stored = repository.GetOwnedGames();
        Assert.Single(stored);
        Assert.Equal(200, stored[0].PlaytimeForever);
    }

    [Fact]
    public void UpsertSchemaPreservesFirstSeenAcrossReSyncs()
    {
        var repository = InMemory();
        var schema = new AchievementSchema("ACH_1", "First", "desc", "i", "g", false, 0);
        var firstSeen = DateTimeOffset.UnixEpoch.AddDays(1);

        repository.UpsertSchema(292030, [schema], firstSeen);
        repository.UpsertSchema(292030, [schema with { DisplayName = "Renamed" }], firstSeen.AddDays(30));

        var stored = repository.GetAchievements(292030).Single();
        Assert.Equal("Renamed", stored.DisplayName);
        Assert.Equal(firstSeen, stored.FirstSeenAt);
    }

    [Fact]
    public void MarkNoAchievementsStopsGameFromBeingSyncedAgain()
    {
        var repository = InMemory();
        repository.UpsertOwnedGames([new OwnedGame(220, "Half-Life 2", "x", 0, 0, null)]);

        repository.MarkNoAchievements(220);

        Assert.False(repository.GetSyncStates()[220].HasAchievements);
    }

    [Fact]
    public void GetGameProgressJoinsSchemaProgressAndRarity()
    {
        var repository = InMemory();
        repository.UpsertOwnedGames([new OwnedGame(292030, "The Witcher 3", "abc", 100, 0, null)]);
        repository.UpsertSchema(292030,
        [
            new AchievementSchema("ACH_1", "First", "", "i", "g", false, 0),
            new AchievementSchema("ACH_2", "Second", "", "i", "g", true, 1),
        ], DateTimeOffset.UnixEpoch);
        repository.UpsertGlobalPercentages(292030, new Dictionary<string, double> { ["ACH_1"] = 62.4, ["ACH_2"] = 0.4 });
        repository.UpsertPlayerAchievements(292030,
        [
            new PlayerAchievement("ACH_1", true, DateTimeOffset.UnixEpoch),
            new PlayerAchievement("ACH_2", false, null),
        ]);

        var progress = repository.GetGameProgress(292030);

        Assert.Equal(2, progress.Count);
        Assert.True(progress.Single(p => p.ApiName == "ACH_1").Unlocked);
        Assert.Equal(0.4, progress.Single(p => p.ApiName == "ACH_2").GlobalPercent);
    }

    [Fact]
    public void WriteSnapshotRecordsCurrentTotals()
    {
        var repository = InMemory();
        repository.UpsertOwnedGames([new OwnedGame(292030, "The Witcher 3", "abc", 100, 0, null)]);
        repository.UpsertSchema(292030,
        [
            new AchievementSchema("ACH_1", "First", "", "i", "g", false, 0),
            new AchievementSchema("ACH_2", "Second", "", "i", "g", false, 1),
        ], DateTimeOffset.UnixEpoch);
        repository.UpsertPlayerAchievements(292030,
        [
            new PlayerAchievement("ACH_1", true, DateTimeOffset.UnixEpoch),
            new PlayerAchievement("ACH_2", false, null),
        ]);

        repository.WriteSnapshot(new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero));

        var unlocked = Dapper.SqlMapper.QuerySingle<long>(
            repository.Connection, "SELECT unlocked_total FROM snapshots");
        var completion = Dapper.SqlMapper.QuerySingle<double>(
            repository.Connection, "SELECT completion_pct FROM snapshots");

        Assert.Equal(1, unlocked);
        Assert.Equal(50, completion, 6);
    }
}
```

Add `public SqliteConnection Connection => _connection;` to `GameRepository` so
tests can assert against raw SQL without adding a read method used only by tests.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter GameRepositoryTests`
Expected: FAIL — `Database` does not exist.

- [ ] **Step 4: Implement the schema and migrations**

`SteamAchievements.Core/Data/Database.cs`:

```csharp
using Dapper;
using Microsoft.Data.Sqlite;

namespace SteamAchievements.Core.Data;

public static class Database
{
    public static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        connection.Execute("PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;");
        Migrate(connection);
        return connection;
    }

    private static void Migrate(SqliteConnection connection)
    {
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS settings (
                id                 INTEGER PRIMARY KEY CHECK (id = 1),
                steam_id64         TEXT,
                persona_name       TEXT,
                avatar_url         TEXT,
                last_full_sync_at  TEXT
            );

            CREATE TABLE IF NOT EXISTS games (
                app_id            INTEGER PRIMARY KEY,
                name              TEXT NOT NULL,
                icon_hash         TEXT NOT NULL DEFAULT '',
                schema_synced_at  TEXT
            );

            CREATE TABLE IF NOT EXISTS owned_games (
                app_id            INTEGER PRIMARY KEY REFERENCES games(app_id) ON DELETE CASCADE,
                playtime_forever  INTEGER NOT NULL DEFAULT 0,
                playtime_2weeks   INTEGER NOT NULL DEFAULT 0,
                last_played_at    TEXT
            );

            CREATE TABLE IF NOT EXISTS achievements (
                app_id         INTEGER NOT NULL REFERENCES games(app_id) ON DELETE CASCADE,
                api_name       TEXT NOT NULL,
                display_name   TEXT NOT NULL DEFAULT '',
                description    TEXT NOT NULL DEFAULT '',
                icon_url       TEXT NOT NULL DEFAULT '',
                icon_gray_url  TEXT NOT NULL DEFAULT '',
                is_hidden      INTEGER NOT NULL DEFAULT 0,
                sort_order     INTEGER NOT NULL DEFAULT 0,
                first_seen_at  TEXT NOT NULL,
                PRIMARY KEY (app_id, api_name)
            );

            CREATE TABLE IF NOT EXISTS global_percents (
                app_id      INTEGER NOT NULL,
                api_name    TEXT NOT NULL,
                percent     REAL NOT NULL,
                fetched_at  TEXT NOT NULL,
                PRIMARY KEY (app_id, api_name)
            );

            CREATE TABLE IF NOT EXISTS player_achievements (
                app_id       INTEGER NOT NULL,
                api_name     TEXT NOT NULL,
                unlocked     INTEGER NOT NULL DEFAULT 0,
                unlocked_at  TEXT,
                PRIMARY KEY (app_id, api_name)
            );

            CREATE TABLE IF NOT EXISTS sync_state (
                app_id            INTEGER PRIMARY KEY,
                has_achievements  INTEGER NOT NULL DEFAULT 1,
                synced_playtime   INTEGER NOT NULL DEFAULT -1,
                schema_synced_at  TEXT,
                global_synced_at  TEXT,
                player_synced_at  TEXT,
                last_error        TEXT
            );

            CREATE TABLE IF NOT EXISTS snapshots (
                taken_at        TEXT PRIMARY KEY,
                unlocked_total  INTEGER NOT NULL,
                avg_rarity      REAL NOT NULL,
                completion_pct  REAL NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_player_achievements_app
                ON player_achievements (app_id);
            """);
    }
}
```

`synced_playtime` defaults to `-1` rather than `0` so that a never-synced game is distinguishable from a game with zero playtime — the planner in Task 7 depends on that distinction.

- [ ] **Step 5: Implement the models and repository**

`SteamAchievements.Core/Data/Models.cs`:

```csharp
namespace SteamAchievements.Core.Data;

public sealed record StoredAchievement(
    string ApiName,
    string DisplayName,
    string Description,
    string IconUrl,
    string IconGrayUrl,
    bool IsHidden,
    int SortOrder,
    DateTimeOffset FirstSeenAt);

public sealed record GameSyncState(
    uint AppId,
    bool HasAchievements,
    int SyncedPlaytime,
    DateTimeOffset? SchemaSyncedAt,
    DateTimeOffset? GlobalSyncedAt,
    DateTimeOffset? PlayerSyncedAt);

public sealed record AchievementProgress(
    string ApiName,
    string DisplayName,
    string Description,
    string IconUrl,
    bool IsHidden,
    bool Unlocked,
    DateTimeOffset? UnlockedAt,
    double? GlobalPercent);
```

`SteamAchievements.Core/Data/GameRepository.cs`:

```csharp
using Dapper;
using Microsoft.Data.Sqlite;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Data;

public sealed class GameRepository
{
    private readonly SqliteConnection _connection;

    public GameRepository(SqliteConnection connection) => _connection = connection;

    /// <summary>Exposed so tests can assert with raw SQL.</summary>
    public SqliteConnection Connection => _connection;

    public void UpsertOwnedGames(IReadOnlyList<OwnedGame> games)
    {
        using var transaction = _connection.BeginTransaction();

        foreach (var game in games)
        {
            _connection.Execute("""
                INSERT INTO games (app_id, name, icon_hash) VALUES (@AppId, @Name, @IconHash)
                ON CONFLICT(app_id) DO UPDATE SET name = excluded.name, icon_hash = excluded.icon_hash;

                INSERT INTO owned_games (app_id, playtime_forever, playtime_2weeks, last_played_at)
                VALUES (@AppId, @PlaytimeForever, @PlaytimeTwoWeeks, @LastPlayed)
                ON CONFLICT(app_id) DO UPDATE SET
                    playtime_forever = excluded.playtime_forever,
                    playtime_2weeks  = excluded.playtime_2weeks,
                    last_played_at   = excluded.last_played_at;

                INSERT INTO sync_state (app_id) VALUES (@AppId)
                ON CONFLICT(app_id) DO NOTHING;
                """,
                new
                {
                    game.AppId,
                    game.Name,
                    game.IconHash,
                    game.PlaytimeForever,
                    game.PlaytimeTwoWeeks,
                    LastPlayed = game.LastPlayed?.ToString("o"),
                }, transaction);
        }

        transaction.Commit();
    }

    // Dapper cannot map into ValueTuple, and it does not translate snake_case
    // to PascalCase by default. Every query therefore aliases its columns
    // explicitly and materializes into a private row record.
    private sealed record OwnedGameRow(uint AppId, string Name, string IconHash,
        int PlaytimeForever, int PlaytimeTwoWeeks, string? LastPlayedAt);

    public IReadOnlyList<OwnedGame> GetOwnedGames() =>
        _connection.Query<OwnedGameRow>("""
            SELECT g.app_id            AS AppId,
                   g.name              AS Name,
                   g.icon_hash         AS IconHash,
                   o.playtime_forever  AS PlaytimeForever,
                   o.playtime_2weeks   AS PlaytimeTwoWeeks,
                   o.last_played_at    AS LastPlayedAt
            FROM owned_games o JOIN games g ON g.app_id = o.app_id
            """)
            .Select(r => new OwnedGame(r.AppId, r.Name, r.IconHash, r.PlaytimeForever, r.PlaytimeTwoWeeks,
                r.LastPlayedAt is null ? null : DateTimeOffset.Parse(r.LastPlayedAt)))
            .ToList();

    public void UpsertSchema(uint appId, IReadOnlyList<AchievementSchema> schema, DateTimeOffset now)
    {
        using var transaction = _connection.BeginTransaction();

        foreach (var achievement in schema)
        {
            // first_seen_at is written once and never overwritten: it is the
            // only signal we will have for future DLC grouping.
            _connection.Execute("""
                INSERT INTO achievements
                    (app_id, api_name, display_name, description, icon_url, icon_gray_url,
                     is_hidden, sort_order, first_seen_at)
                VALUES (@AppId, @ApiName, @DisplayName, @Description, @IconUrl, @IconGrayUrl,
                        @IsHidden, @SortOrder, @Now)
                ON CONFLICT(app_id, api_name) DO UPDATE SET
                    display_name  = excluded.display_name,
                    description   = excluded.description,
                    icon_url      = excluded.icon_url,
                    icon_gray_url = excluded.icon_gray_url,
                    is_hidden     = excluded.is_hidden,
                    sort_order    = excluded.sort_order;
                """,
                new
                {
                    AppId = appId,
                    achievement.ApiName,
                    achievement.DisplayName,
                    achievement.Description,
                    achievement.IconUrl,
                    achievement.IconGrayUrl,
                    IsHidden = achievement.IsHidden ? 1 : 0,
                    achievement.SortOrder,
                    Now = now.ToString("o"),
                }, transaction);
        }

        _connection.Execute(
            "UPDATE sync_state SET schema_synced_at = @Now WHERE app_id = @AppId",
            new { AppId = appId, Now = now.ToString("o") }, transaction);

        transaction.Commit();
    }

    private sealed record AchievementRow(string ApiName, string DisplayName, string Description,
        string IconUrl, string IconGrayUrl, long IsHidden, int SortOrder, string FirstSeenAt);

    public IReadOnlyList<StoredAchievement> GetAchievements(uint appId) =>
        _connection.Query<AchievementRow>("""
            SELECT api_name       AS ApiName,
                   display_name   AS DisplayName,
                   description    AS Description,
                   icon_url       AS IconUrl,
                   icon_gray_url  AS IconGrayUrl,
                   is_hidden      AS IsHidden,
                   sort_order     AS SortOrder,
                   first_seen_at  AS FirstSeenAt
            FROM achievements WHERE app_id = @AppId ORDER BY sort_order
            """, new { AppId = appId })
            .Select(r => new StoredAchievement(r.ApiName, r.DisplayName, r.Description, r.IconUrl,
                r.IconGrayUrl, r.IsHidden == 1, r.SortOrder, DateTimeOffset.Parse(r.FirstSeenAt)))
            .ToList();

    public void UpsertGlobalPercentages(uint appId, IReadOnlyDictionary<string, double> percentages)
    {
        using var transaction = _connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToString("o");

        foreach (var (apiName, percent) in percentages)
        {
            _connection.Execute("""
                INSERT INTO global_percents (app_id, api_name, percent, fetched_at)
                VALUES (@AppId, @ApiName, @Percent, @Now)
                ON CONFLICT(app_id, api_name) DO UPDATE SET
                    percent = excluded.percent, fetched_at = excluded.fetched_at;
                """, new { AppId = appId, ApiName = apiName, Percent = percent, Now = now }, transaction);
        }

        _connection.Execute("UPDATE sync_state SET global_synced_at = @Now WHERE app_id = @AppId",
            new { AppId = appId, Now = now }, transaction);
        transaction.Commit();
    }

    public void UpsertPlayerAchievements(uint appId, IReadOnlyList<PlayerAchievement> progress)
    {
        using var transaction = _connection.BeginTransaction();

        foreach (var item in progress)
        {
            _connection.Execute("""
                INSERT INTO player_achievements (app_id, api_name, unlocked, unlocked_at)
                VALUES (@AppId, @ApiName, @Unlocked, @UnlockedAt)
                ON CONFLICT(app_id, api_name) DO UPDATE SET
                    unlocked = excluded.unlocked, unlocked_at = excluded.unlocked_at;
                """,
                new
                {
                    AppId = appId,
                    item.ApiName,
                    Unlocked = item.Unlocked ? 1 : 0,
                    UnlockedAt = item.UnlockedAt?.ToString("o"),
                }, transaction);
        }

        transaction.Commit();
    }

    private sealed record SyncStateRow(uint AppId, long HasAchievements, int SyncedPlaytime,
        string? SchemaSyncedAt, string? GlobalSyncedAt, string? PlayerSyncedAt);

    public IReadOnlyDictionary<uint, GameSyncState> GetSyncStates() =>
        _connection.Query<SyncStateRow>("""
            SELECT app_id            AS AppId,
                   has_achievements  AS HasAchievements,
                   synced_playtime   AS SyncedPlaytime,
                   schema_synced_at  AS SchemaSyncedAt,
                   global_synced_at  AS GlobalSyncedAt,
                   player_synced_at  AS PlayerSyncedAt
            FROM sync_state
            """)
            .ToDictionary(r => r.AppId, r => new GameSyncState(
                r.AppId,
                r.HasAchievements == 1,
                r.SyncedPlaytime,
                r.SchemaSyncedAt is null ? null : DateTimeOffset.Parse(r.SchemaSyncedAt),
                r.GlobalSyncedAt is null ? null : DateTimeOffset.Parse(r.GlobalSyncedAt),
                r.PlayerSyncedAt is null ? null : DateTimeOffset.Parse(r.PlayerSyncedAt)));

    public void MarkSynced(uint appId, int playtime, DateTimeOffset now) =>
        _connection.Execute("""
            UPDATE sync_state
            SET synced_playtime = @Playtime, player_synced_at = @Now, last_error = NULL
            WHERE app_id = @AppId
            """, new { AppId = appId, Playtime = playtime, Now = now.ToString("o") });

    public void MarkNoAchievements(uint appId) =>
        _connection.Execute("UPDATE sync_state SET has_achievements = 0 WHERE app_id = @AppId",
            new { AppId = appId });

    public void MarkError(uint appId, string error) =>
        _connection.Execute("UPDATE sync_state SET last_error = @Error WHERE app_id = @AppId",
            new { AppId = appId, Error = error });

    private sealed record ProgressRow(string ApiName, string DisplayName, string Description,
        string IconUrl, long IsHidden, long? Unlocked, string? UnlockedAt, double? Percent);

    public IReadOnlyList<AchievementProgress> GetGameProgress(uint appId) =>
        _connection.Query<ProgressRow>("""
            SELECT a.api_name      AS ApiName,
                   a.display_name  AS DisplayName,
                   a.description   AS Description,
                   a.icon_url      AS IconUrl,
                   a.is_hidden     AS IsHidden,
                   p.unlocked      AS Unlocked,
                   p.unlocked_at   AS UnlockedAt,
                   gp.percent      AS Percent
            FROM achievements a
            LEFT JOIN player_achievements p ON p.app_id = a.app_id AND p.api_name = a.api_name
            LEFT JOIN global_percents     gp ON gp.app_id = a.app_id AND gp.api_name = a.api_name
            WHERE a.app_id = @AppId
            ORDER BY a.sort_order
            """, new { AppId = appId })
            .Select(r => new AchievementProgress(r.ApiName, r.DisplayName, r.Description, r.IconUrl,
                r.IsHidden == 1, r.Unlocked == 1,
                r.UnlockedAt is null ? null : DateTimeOffset.Parse(r.UnlockedAt), r.Percent))
            .ToList();

    /// <summary>
    /// One row per sync. Trend charts are out of scope for the MVP, but this
    /// history cannot be backfilled later, so it is recorded from day one.
    /// </summary>
    public void WriteSnapshot(DateTimeOffset takenAt)
    {
        _connection.Execute("""
            INSERT INTO snapshots (taken_at, unlocked_total, avg_rarity, completion_pct)
            SELECT @TakenAt,
                   COALESCE(SUM(p.unlocked), 0),
                   COALESCE(AVG(CASE WHEN p.unlocked = 1 THEN gp.percent END), 0),
                   COALESCE(100.0 * SUM(p.unlocked) / NULLIF(COUNT(*), 0), 0)
            FROM achievements a
            LEFT JOIN player_achievements p ON p.app_id = a.app_id AND p.api_name = a.api_name
            LEFT JOIN global_percents     gp ON gp.app_id = a.app_id AND gp.api_name = a.api_name
            ON CONFLICT(taken_at) DO NOTHING;
            """, new { TakenAt = takenAt.ToString("o") });
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter GameRepositoryTests`
Expected: PASS, 6 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add SQLite storage and repository"
```

---

### Task 7: Sync planner

Pure logic, no I/O — this is where the thousand-fold reduction in request count lives, so it deserves its own tests independent of any network code.

**Files:**
- Create: `SteamAchievements.Core/Sync/SyncPlanner.cs`, `SteamAchievements.Core/Sync/SyncOptions.cs`
- Test: `SteamAchievements.Core.Tests/Sync/SyncPlannerTests.cs`

**Interfaces:**
- Consumes: `OwnedGame` (Task 5), `GameSyncState` (Task 6)
- Produces: `record SyncWorkItem(uint AppId, int Playtime, bool NeedSchema, bool NeedGlobal, bool NeedPlayer)`; `record SyncOptions(TimeSpan SchemaTtl, TimeSpan GlobalTtl)` with `SyncOptions.Default`; `SyncPlanner.Plan(IReadOnlyList<OwnedGame>, IReadOnlyDictionary<uint, GameSyncState>, DateTimeOffset now, SyncOptions, bool force) → IReadOnlyList<SyncWorkItem>`

- [ ] **Step 1: Write the failing tests**

`SteamAchievements.Core.Tests/Sync/SyncPlannerTests.cs`:

```csharp
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Sync;

namespace SteamAchievements.Core.Tests.Sync;

public class SyncPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    private static OwnedGame Game(uint appId, int playtime) =>
        new(appId, $"Game {appId}", "icon", playtime, 0, null);

    private static GameSyncState State(uint appId, int syncedPlaytime, bool hasAchievements = true,
        DateTimeOffset? schemaAt = null, DateTimeOffset? globalAt = null) =>
        new(appId, hasAchievements, syncedPlaytime, schemaAt ?? Now, globalAt ?? Now, Now);

    [Fact]
    public void PlansEverythingForNeverSyncedGame()
    {
        var plan = SyncPlanner.Plan([Game(292030, 100)], new Dictionary<uint, GameSyncState>(),
            Now, SyncOptions.Default, force: false);

        var item = Assert.Single(plan);
        Assert.True(item.NeedSchema);
        Assert.True(item.NeedGlobal);
        Assert.True(item.NeedPlayer);
    }

    [Fact]
    public void SkipsGameWhosePlaytimeDidNotChange()
    {
        var plan = SyncPlanner.Plan(
            [Game(292030, 100)],
            new Dictionary<uint, GameSyncState> { [292030] = State(292030, syncedPlaytime: 100) },
            Now, SyncOptions.Default, force: false);

        Assert.Empty(plan);
    }

    [Fact]
    public void RequestsPlayerProgressWhenPlaytimeIncreased()
    {
        var plan = SyncPlanner.Plan(
            [Game(292030, 160)],
            new Dictionary<uint, GameSyncState> { [292030] = State(292030, syncedPlaytime: 100) },
            Now, SyncOptions.Default, force: false);

        var item = Assert.Single(plan);
        Assert.True(item.NeedPlayer);
        Assert.False(item.NeedSchema);
    }

    [Fact]
    public void ExcludesGamesKnownToHaveNoAchievements()
    {
        var plan = SyncPlanner.Plan(
            [Game(220, 500)],
            new Dictionary<uint, GameSyncState> { [220] = State(220, syncedPlaytime: 100, hasAchievements: false) },
            Now, SyncOptions.Default, force: false);

        Assert.Empty(plan);
    }

    [Fact]
    public void RefreshesSchemaAfterTtlExpires()
    {
        var plan = SyncPlanner.Plan(
            [Game(292030, 100)],
            new Dictionary<uint, GameSyncState>
            {
                [292030] = State(292030, syncedPlaytime: 100, schemaAt: Now.AddDays(-31)),
            },
            Now, SyncOptions.Default, force: false);

        var item = Assert.Single(plan);
        Assert.True(item.NeedSchema);
        Assert.False(item.NeedPlayer);
    }

    [Fact]
    public void RefreshesGlobalPercentagesAfterTtlExpires()
    {
        var plan = SyncPlanner.Plan(
            [Game(292030, 100)],
            new Dictionary<uint, GameSyncState>
            {
                [292030] = State(292030, syncedPlaytime: 100, globalAt: Now.AddDays(-8)),
            },
            Now, SyncOptions.Default, force: false);

        Assert.True(Assert.Single(plan).NeedGlobal);
    }

    [Fact]
    public void ForceRequestsEverythingForEveryGameWithAchievements()
    {
        var plan = SyncPlanner.Plan(
            [Game(292030, 100), Game(220, 0)],
            new Dictionary<uint, GameSyncState>
            {
                [292030] = State(292030, syncedPlaytime: 100),
                [220] = State(220, syncedPlaytime: 0, hasAchievements: false),
            },
            Now, SyncOptions.Default, force: true);

        // Force refreshes everything we can, but still respects the permanent
        // "this app has no stats" fact — re-asking would waste hundreds of calls.
        var item = Assert.Single(plan);
        Assert.Equal(292030u, item.AppId);
        Assert.True(item is { NeedSchema: true, NeedGlobal: true, NeedPlayer: true });
    }

    [Fact]
    public void OrdersRecentlyPlayedGamesFirst()
    {
        var plan = SyncPlanner.Plan(
            [Game(1, 10), Game(2, 5000), Game(3, 200)],
            new Dictionary<uint, GameSyncState>(),
            Now, SyncOptions.Default, force: false);

        Assert.Equal([2u, 3u, 1u], plan.Select(i => i.AppId));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter SyncPlannerTests`
Expected: FAIL — `SyncPlanner` does not exist.

- [ ] **Step 3: Implement the options**

`SteamAchievements.Core/Sync/SyncOptions.cs`:

```csharp
namespace SteamAchievements.Core.Sync;

public sealed record SyncOptions(TimeSpan SchemaTtl, TimeSpan GlobalTtl)
{
    public static SyncOptions Default { get; } = new(TimeSpan.FromDays(30), TimeSpan.FromDays(7));
}
```

- [ ] **Step 4: Implement the planner**

`SteamAchievements.Core/Sync/SyncPlanner.cs`:

```csharp
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Sync;

public sealed record SyncWorkItem(uint AppId, int Playtime, bool NeedSchema, bool NeedGlobal, bool NeedPlayer);

/// <summary>
/// Decides what actually needs fetching. Pure function of current library
/// state and previous sync state — no I/O, no clock.
/// </summary>
public static class SyncPlanner
{
    public static IReadOnlyList<SyncWorkItem> Plan(
        IReadOnlyList<OwnedGame> owned,
        IReadOnlyDictionary<uint, GameSyncState> states,
        DateTimeOffset now,
        SyncOptions options,
        bool force)
    {
        var items = new List<SyncWorkItem>();

        foreach (var game in owned.OrderByDescending(g => g.PlaytimeForever))
        {
            states.TryGetValue(game.AppId, out var state);

            // Once Steam says an app has no stats, that never changes.
            // Re-asking would burn hundreds of requests per sync.
            if (state is { HasAchievements: false })
            {
                continue;
            }

            var neverSynced = state is null || state.SyncedPlaytime < 0;

            var needSchema = force || neverSynced || state!.SchemaSyncedAt is null
                || now - state.SchemaSyncedAt.Value > options.SchemaTtl;

            var needGlobal = force || neverSynced || state!.GlobalSyncedAt is null
                || now - state.GlobalSyncedAt.Value > options.GlobalTtl;

            // The core optimization: unchanged playtime means unchanged achievements.
            var needPlayer = force || neverSynced || game.PlaytimeForever != state!.SyncedPlaytime;

            if (needSchema || needGlobal || needPlayer)
            {
                items.Add(new SyncWorkItem(game.AppId, game.PlaytimeForever, needSchema, needGlobal, needPlayer));
            }
        }

        return items;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter SyncPlannerTests`
Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add sync planner with playtime-based skipping"
```

---

### Task 8: Sync orchestrator

**Files:**
- Create: `SteamAchievements.Core/Sync/SyncOrchestrator.cs`, `SteamAchievements.Core/Sync/SyncProgress.cs`, `SteamAchievements.Core/Sync/RateLimiter.cs`
- Modify: `SteamAchievements.Core/SteamAchievements.Core.csproj` (add `Polly`)
- Test: `SteamAchievements.Core.Tests/Sync/SyncOrchestratorTests.cs`

**Interfaces:**
- Consumes: `SteamApiClient`, `GameRepository`, `SyncPlanner`, `SteamApiException`
- Produces: `record SyncProgress(int Completed, int Total, string CurrentGame)`; `SyncOrchestrator(SteamApiClient, GameRepository, SyncOptions)` with `Task RunAsync(ulong steamId, bool force, IProgress<SyncProgress>?, CancellationToken)`

- [ ] **Step 1: Add Polly**

```bash
dotnet add SteamAchievements.Core package Polly
```

- [ ] **Step 2: Write the failing tests**

`SteamAchievements.Core.Tests/Sync/SyncOrchestratorTests.cs`:

```csharp
using System.Net;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Sync;
using SteamAchievements.Core.Tests.Steam;

namespace SteamAchievements.Core.Tests.Sync;

public class SyncOrchestratorTests
{
    private static readonly ulong SteamId = 76561190000000002;

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static HttpResponseMessage NoStats() =>
        new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("<html><body>Requested app has no stats</body></html>",
                System.Text.Encoding.UTF8, "text/html"),
        };

    private static async Task<(SyncOrchestrator Sync, GameRepository Repo, FakeHttpMessageHandler Handler)> Build()
    {
        var owned = await File.ReadAllTextAsync(TestPaths.Data("owned_games.json"));
        var schema = await File.ReadAllTextAsync(TestPaths.Data("schema_for_game.json"));
        var player = await File.ReadAllTextAsync(TestPaths.Data("player_achievements.json"));
        var global = await File.ReadAllTextAsync(TestPaths.Data("global_percentages.json"));

        var handler = new FakeHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("GetOwnedGames")) return Json(owned);
            if (url.Contains("GetSchemaForGame")) return url.Contains("appid=220") ? NoStats() : Json(schema);
            if (url.Contains("GetPlayerAchievements")) return url.Contains("appid=220") ? NoStats() : Json(player);
            if (url.Contains("GetGlobalAchievementPercentages")) return Json(global);

            throw new InvalidOperationException($"Unexpected request: {url}");
        });

        var client = new SteamApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "TESTKEY");
        var repository = new GameRepository(Database.Open(":memory:"));

        return (new SyncOrchestrator(client, repository, SyncOptions.Default), repository, handler);
    }

    [Fact]
    public async Task StoresLibraryAndAchievementsOnFirstRun()
    {
        var (sync, repository, _) = await Build();

        await sync.RunAsync(SteamId, force: false, progress: null, CancellationToken.None);

        Assert.Equal(2, repository.GetOwnedGames().Count);
        Assert.Equal(2, repository.GetGameProgress(292030).Count);

        var snapshots = Dapper.SqlMapper.QuerySingle<long>(
            repository.Connection, "SELECT COUNT(*) FROM snapshots");
        Assert.Equal(1, snapshots);
    }

    [Fact]
    public async Task MarksGamesWithoutAchievementsAndStopsQueryingThem()
    {
        var (sync, repository, handler) = await Build();

        await sync.RunAsync(SteamId, force: false, progress: null, CancellationToken.None);
        var requestsAfterFirst = handler.Requests.Count;
        await sync.RunAsync(SteamId, force: false, progress: null, CancellationToken.None);

        Assert.False(repository.GetSyncStates()[220].HasAchievements);

        // Second run must only re-fetch the library itself.
        Assert.Equal(requestsAfterFirst + 1, handler.Requests.Count);
    }

    [Fact]
    public async Task ReportsProgressForEveryProcessedGame()
    {
        var (sync, _, _) = await Build();
        var reports = new List<SyncProgress>();

        await sync.RunAsync(SteamId, force: false, new Progress<SyncProgress>(reports.Add), CancellationToken.None);

        await Task.Delay(50); // Progress<T> marshals asynchronously
        Assert.NotEmpty(reports);
        Assert.Equal(reports[^1].Total, reports[^1].Completed);
    }

    [Fact]
    public async Task StopsWhenCancelled()
    {
        var (sync, _, _) = await Build();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sync.RunAsync(SteamId, force: false, progress: null, cancellation.Token));
    }

    [Fact]
    public async Task DoesNotRetryOnInvalidKey()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("<html><body>Access is denied.</body></html>",
                System.Text.Encoding.UTF8, "text/html"),
        });

        var sync = new SyncOrchestrator(
            new SteamApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") }, "BAD"),
            new GameRepository(Database.Open(":memory:")),
            SyncOptions.Default);

        await Assert.ThrowsAsync<SteamApiException>(
            () => sync.RunAsync(SteamId, force: false, progress: null, CancellationToken.None));

        Assert.Single(handler.Requests);
    }
}
```

The retry test is the important one: an invalid key retried with backoff turns an instant, clear error into a 15-second hang followed by the same error.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter SyncOrchestratorTests`
Expected: FAIL — `SyncOrchestrator` does not exist.

- [ ] **Step 4: Implement the rate limiter and progress record**

`SteamAchievements.Core/Sync/SyncProgress.cs`:

```csharp
namespace SteamAchievements.Core.Sync;

public sealed record SyncProgress(int Completed, int Total, string CurrentGame);
```

`SteamAchievements.Core/Sync/RateLimiter.cs`:

```csharp
namespace SteamAchievements.Core.Sync;

/// <summary>
/// Simple token bucket. Steam tolerates roughly five requests per second
/// before answering 429.
/// </summary>
public sealed class RateLimiter
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _interval;
    private DateTimeOffset _nextSlot = DateTimeOffset.MinValue;

    public RateLimiter(double requestsPerSecond) =>
        _interval = TimeSpan.FromSeconds(1 / requestsPerSecond);

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var now = DateTimeOffset.UtcNow;
            var wait = _nextSlot - now;

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
                now = DateTimeOffset.UtcNow;
            }

            _nextSlot = now + _interval;
        }
        finally
        {
            _gate.Release();
        }
    }
}
```

- [ ] **Step 5: Implement the orchestrator**

`SteamAchievements.Core/Sync/SyncOrchestrator.cs`:

```csharp
using Polly;
using Polly.Retry;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Sync;

public sealed class SyncOrchestrator
{
    private const int WorkerCount = 4;

    private readonly SteamApiClient _client;
    private readonly GameRepository _repository;
    private readonly SyncOptions _options;
    private readonly RateLimiter _rateLimiter = new(requestsPerSecond: 5);
    private readonly ResiliencePipeline _retry;

    public SyncOrchestrator(SteamApiClient client, GameRepository repository, SyncOptions options)
    {
        _client = client;
        _repository = repository;
        _options = options;

        _retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                // Only transient failures are worth retrying. An invalid key
                // is permanent and must surface immediately.
                ShouldHandle = new PredicateBuilder()
                    .Handle<SteamApiException>(e => e.IsTransient),
                MaxRetryAttempts = 4,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(1),
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                // Polly v8 breaks on a failure ratio within a sampling window
                // rather than on N consecutive failures, so this approximates
                // the spec's "five in a row": every call failing across a
                // 30-second window with at least five calls trips the breaker
                // and stops hammering Steam.
                ShouldHandle = new PredicateBuilder()
                    .Handle<SteamApiException>(e => e.IsTransient),
                FailureRatio = 1.0,
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30),
            })
            .Build();
    }

    public async Task RunAsync(
        ulong steamId,
        bool force,
        IProgress<SyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var owned = await _retry.ExecuteAsync(
            async token => await _client.GetOwnedGamesAsync(steamId, token), cancellationToken);

        _repository.UpsertOwnedGames(owned);

        var plan = SyncPlanner.Plan(
            owned, _repository.GetSyncStates(), DateTimeOffset.UtcNow, _options, force);

        var names = owned.ToDictionary(g => g.AppId, g => g.Name);
        var completed = 0;

        await Parallel.ForEachAsync(
            plan,
            new ParallelOptions { MaxDegreeOfParallelism = WorkerCount, CancellationToken = cancellationToken },
            async (item, token) =>
            {
                await SyncGameAsync(steamId, item, token);

                var done = Interlocked.Increment(ref completed);
                progress?.Report(new SyncProgress(done, plan.Count, names.GetValueOrDefault(item.AppId, string.Empty)));
            });

        // Trend charts come later, but the history behind them cannot be
        // reconstructed after the fact — record it from the very first sync.
        _repository.WriteSnapshot(DateTimeOffset.UtcNow);
    }

    private async Task SyncGameAsync(ulong steamId, SyncWorkItem item, CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;

            if (item.NeedSchema)
            {
                await _rateLimiter.WaitAsync(cancellationToken);
                var schema = await _retry.ExecuteAsync(
                    async token => await _client.GetSchemaForGameAsync(item.AppId, token), cancellationToken);

                if (schema.Count == 0)
                {
                    _repository.MarkNoAchievements(item.AppId);
                    return;
                }

                _repository.UpsertSchema(item.AppId, schema, now);
            }

            if (item.NeedGlobal)
            {
                await _rateLimiter.WaitAsync(cancellationToken);
                var percentages = await _retry.ExecuteAsync(
                    async token => await _client.GetGlobalPercentagesAsync(item.AppId, token), cancellationToken);

                _repository.UpsertGlobalPercentages(item.AppId, percentages);
            }

            if (item.NeedPlayer)
            {
                await _rateLimiter.WaitAsync(cancellationToken);
                var progress = await _retry.ExecuteAsync(
                    async token => await _client.GetPlayerAchievementsAsync(steamId, item.AppId, token), cancellationToken);

                _repository.UpsertPlayerAchievements(item.AppId, progress);
            }

            // Written per game, which is what makes an interrupted sync resumable.
            _repository.MarkSynced(item.AppId, item.Playtime, now);
        }
        catch (SteamApiException e) when (e.Kind == SteamApiErrorKind.NoStatsForApp)
        {
            // Expected for 30-40% of a library: soundtracks, demos, tools.
            _repository.MarkNoAchievements(item.AppId);
        }
        catch (SteamApiException e) when (e.Kind != SteamApiErrorKind.InvalidKey)
        {
            // One bad game must not abort the whole sync; an invalid key must.
            _repository.MarkError(item.AppId, e.Message);
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter SyncOrchestratorTests`
Expected: PASS, 5 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add resumable sync orchestrator with rate limiting"
```

---

### Task 9: Effort calculator

The product's core value, and pure enough to be exhaustively tested.

**Files:**
- Create: `SteamAchievements.Core/Analytics/EffortCalculator.cs`
- Test: `SteamAchievements.Core.Tests/Analytics/EffortCalculatorTests.cs`

**Interfaces:**
- Consumes: `AchievementProgress` (Task 6)
- Produces: `record GameEffort(double RemainingEffort, int RemainingCount, int UnlockedCount, int TotalCount, bool HasBlockers, bool RarityUnknown, double CompletionPercent)`; `EffortCalculator.Cost(double percent, double maxPercent) → double`; `EffortCalculator.Evaluate(IReadOnlyList<AchievementProgress>) → GameEffort`

- [ ] **Step 1: Write the failing tests**

`SteamAchievements.Core.Tests/Analytics/EffortCalculatorTests.cs`:

```csharp
using SteamAchievements.Core.Analytics;
using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Tests.Analytics;

public class EffortCalculatorTests
{
    private static AchievementProgress Achievement(string name, bool unlocked, double? percent) =>
        new(name, name, string.Empty, string.Empty, false, unlocked, unlocked ? DateTimeOffset.UnixEpoch : null, percent);

    [Fact]
    public void MostCommonAchievementInAGameCostsNothing()
    {
        Assert.Equal(0, EffortCalculator.Cost(percent: 55, maxPercent: 55), 6);
    }

    [Fact]
    public void HalvingRelativeRarityAddsExactlyOne()
    {
        Assert.Equal(1, EffortCalculator.Cost(percent: 27.5, maxPercent: 55), 6);
        Assert.Equal(2, EffortCalculator.Cost(percent: 13.75, maxPercent: 55), 6);
    }

    [Fact]
    public void ZeroPercentIsClampedInsteadOfBecomingInfinite()
    {
        var cost = EffortCalculator.Cost(percent: 0, maxPercent: 55);

        Assert.True(double.IsFinite(cost));
        Assert.True(cost > 0);
    }

    [Fact]
    public void CountsOnlyLockedAchievementsTowardsEffort()
    {
        var effort = EffortCalculator.Evaluate(
        [
            Achievement("A", unlocked: true, percent: 50),
            Achievement("B", unlocked: false, percent: 25),
        ]);

        Assert.Equal(1, effort.RemainingCount);
        Assert.Equal(1, effort.UnlockedCount);
        Assert.Equal(2, effort.TotalCount);
        Assert.Equal(1, effort.RemainingEffort, 6);   // 25 / 50 → -log2(0.5) = 1
    }

    [Fact]
    public void ManyEasyAchievementsCostLessThanFewRareOnes()
    {
        var many = EffortCalculator.Evaluate(Enumerable.Range(0, 20)
            .Select(i => Achievement($"A{i}", unlocked: false, percent: i == 0 ? 60 : 55))
            .ToList());

        var few = EffortCalculator.Evaluate(
        [
            Achievement("Common", unlocked: true, percent: 60),
            Achievement("Rare1", unlocked: false, percent: 0.6),
            Achievement("Rare2", unlocked: false, percent: 0.6),
            Achievement("Rare3", unlocked: false, percent: 0.6),
        ]);

        Assert.True(many.RemainingEffort < few.RemainingEffort);
    }

    [Fact]
    public void FlagsBlockersBelowTwoPercentRelativeRarity()
    {
        var effort = EffortCalculator.Evaluate(
        [
            Achievement("Common", unlocked: true, percent: 50),
            Achievement("Dead", unlocked: false, percent: 0.4),   // 0.8% relative
        ]);

        Assert.True(effort.HasBlockers);
    }

    [Fact]
    public void DoesNotFlagBlockersThatAreAlreadyUnlocked()
    {
        var effort = EffortCalculator.Evaluate(
        [
            Achievement("Common", unlocked: true, percent: 50),
            Achievement("Rare", unlocked: true, percent: 0.4),
        ]);

        Assert.False(effort.HasBlockers);
    }

    [Fact]
    public void FallsBackToEqualWeightsWhenRarityIsUnknown()
    {
        var effort = EffortCalculator.Evaluate(
        [
            Achievement("A", unlocked: true, percent: null),
            Achievement("B", unlocked: false, percent: null),
            Achievement("C", unlocked: false, percent: null),
        ]);

        Assert.True(effort.RarityUnknown);
        Assert.Equal(2, effort.RemainingEffort, 6);   // one unit per locked achievement
        Assert.False(effort.HasBlockers);
    }

    [Fact]
    public void ReportsCompletionPercent()
    {
        var effort = EffortCalculator.Evaluate(
        [
            Achievement("A", unlocked: true, percent: 50),
            Achievement("B", unlocked: true, percent: 40),
            Achievement("C", unlocked: false, percent: 30),
            Achievement("D", unlocked: false, percent: 20),
        ]);

        Assert.Equal(50, effort.CompletionPercent, 6);
    }

    [Fact]
    public void HandlesGameWithNoAchievements()
    {
        var effort = EffortCalculator.Evaluate([]);

        Assert.Equal(0, effort.TotalCount);
        Assert.Equal(0, effort.RemainingEffort);
        Assert.Equal(0, effort.CompletionPercent);
    }

    [Fact]
    public void HandlesFullyCompletedGame()
    {
        var effort = EffortCalculator.Evaluate(
        [
            Achievement("A", unlocked: true, percent: 50),
            Achievement("B", unlocked: true, percent: 10),
        ]);

        Assert.Equal(0, effort.RemainingEffort);
        Assert.Equal(100, effort.CompletionPercent, 6);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter EffortCalculatorTests`
Expected: FAIL — `EffortCalculator` does not exist.

- [ ] **Step 3: Implement the calculator**

`SteamAchievements.Core/Analytics/EffortCalculator.cs`:

```csharp
using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Analytics;

public sealed record GameEffort(
    double RemainingEffort,
    int RemainingCount,
    int UnlockedCount,
    int TotalCount,
    bool HasBlockers,
    bool RarityUnknown,
    double CompletionPercent);

/// <summary>
/// Ranks games by how much work is left rather than by completion percentage.
///
/// Steam computes global achievement percentages across everyone who owns a
/// game, including people who never launched it, so raw percentages are not
/// comparable between titles. Normalizing against the game's own most common
/// achievement removes that distortion.
/// </summary>
public static class EffortCalculator
{
    /// <summary>Relative rarity below this marks an achievement as a blocker.</summary>
    private const double BlockerThreshold = 0.02;

    /// <summary>Floor for relative rarity; without it a 0% achievement yields infinity.</summary>
    private const double RarityFloor = 0.001;

    public static double Cost(double percent, double maxPercent)
    {
        if (maxPercent <= 0)
        {
            return 1;
        }

        var relative = Math.Max(percent / maxPercent, RarityFloor);
        return -Math.Log2(Math.Min(relative, 1));
    }

    public static GameEffort Evaluate(IReadOnlyList<AchievementProgress> achievements)
    {
        if (achievements.Count == 0)
        {
            return new GameEffort(0, 0, 0, 0, false, false, 0);
        }

        var unlocked = achievements.Count(a => a.Unlocked);
        var locked = achievements.Where(a => !a.Unlocked).ToList();
        var completion = 100.0 * unlocked / achievements.Count;

        var known = achievements.Where(a => a.GlobalPercent is > 0).ToList();

        if (known.Count == 0)
        {
            // No rarity data at all — every remaining achievement counts as one unit.
            return new GameEffort(locked.Count, locked.Count, unlocked, achievements.Count,
                HasBlockers: false, RarityUnknown: true, completion);
        }

        var maxPercent = known.Max(a => a.GlobalPercent!.Value);
        var effort = 0.0;
        var hasBlockers = false;

        foreach (var achievement in locked)
        {
            var percent = achievement.GlobalPercent ?? 0;
            effort += Cost(percent, maxPercent);

            if (percent / maxPercent < BlockerThreshold)
            {
                hasBlockers = true;
            }
        }

        return new GameEffort(effort, locked.Count, unlocked, achievements.Count,
            hasBlockers, RarityUnknown: false, completion);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter EffortCalculatorTests`
Expected: PASS, 11 tests.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test`
Expected: PASS, 47 tests total.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add effort calculator for completion ranking"
```

---

## Manual verification on Windows

After Task 9, one manual pass is needed before the UI plan starts, because
everything above was tested against fixtures rather than a live account.

- [ ] Capture real fixtures on the Windows machine using a real API key, then
      anonymize them (strip `key=`, replace `steamid` with `76561190000000002`)
      and replace the synthetic fixtures in `testdata/`.
- [ ] Copy the real `config/loginusers.vdf` into `testdata/`, replacing the
      SteamIDs and account names, and confirm `LoginUsersReaderTests` still
      passes against it.
- [ ] Write a temporary console harness that runs `SyncOrchestrator` against
      the real library, and confirm: total request count is in the expected
      range, a second run issues only one request, and an interrupted run
      resumes rather than restarting.

## Follow-up plan

The next plan covers the Windows host and the UI: `ISteamPathProvider` over
the registry, `ISecretStore` over DPAPI, the clipboard watcher, the onboarding
wizard, the completion queue screen and the game screen. It is written
separately because those pieces can only be verified through CI on Windows,
whereas everything in this plan runs locally in seconds.
