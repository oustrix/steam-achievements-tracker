# Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the application a log file, so that its first run on Windows produces evidence instead of guesses.

**Architecture:** `Microsoft.Extensions.Logging.Abstractions` is the seam; call sites take `ILogger<T>`. The file sink — formatting, redaction, rotation, flushing, failure handling — lives in `Core/Diagnostics` and is covered by `dotnet test` on macOS. `SteamAchievements.Windows` contributes only the log directory, the WebView2 version string, and three unhandled-exception hooks.

**Tech Stack:** .NET 10, xUnit, Microsoft.Extensions.Logging.Abstractions 10.0.10, Microsoft.Data.Sqlite, Blazor (Razor Class Library), WPF.

Design: `docs/superpowers/specs/2026-07-27-diagnostics-design.md`.

## Global Constraints

- Everything committed is in **English** — code, comments, documentation, commit messages, UI strings.
- `SteamAchievements.Windows` (`net10.0-windows`) **does not compile on macOS**. Never try. Task 11 is verified by CI and by the Windows pass, not locally.
- Always name the project: `dotnet test tests/SteamAchievements.Core.Tests`, `dotnet build src/SteamAchievements.UI`, `dotnet format src/SteamAchievements.Core`. A bare `dotnet test` or `dotnet format` at the repository root fails with NETSDK1100 because the solution includes the WPF project. That failure is about the host platform, not about the change.
- Package version for the new reference: `Microsoft.Extensions.Logging.Abstractions` **10.0.10**, matching the version already in the WPF host's restored graph.
- **Loggers are required constructor parameters, never optional and never defaulted to `NullLogger`.** Tests pass `NullLogger<T>.Instance` explicitly.
- **Nothing ever passes a Steam Web API key to a logger.** Code that must mention the key logs whether one is stored and its length.
- Line endings inside the log file are `\r\n`, hardcoded, never `Environment.NewLine` — the file is read on Windows and the tests run on macOS and Linux.
- The 316 existing tests must keep passing after every task.
- Run `dotnet format <project>` on each project touched before committing.

---

### Task 1: The logging package and `Redaction`

**Files:**
- Modify: `src/SteamAchievements.Core/SteamAchievements.Core.csproj`
- Create: `src/SteamAchievements.Core/Diagnostics/Redaction.cs`
- Test: `tests/SteamAchievements.Core.Tests/Diagnostics/RedactionTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `SteamAchievements.Core.Diagnostics.Redaction.Scrub(string text) -> string`, a pure static used by Task 4.

- [ ] **Step 1: Add the package reference**

In `src/SteamAchievements.Core/SteamAchievements.Core.csproj`, inside the existing first `<ItemGroup>` that holds `Dapper` and `Microsoft.Data.Sqlite`, add:

```xml
    <!--
      The seam for logging. Not a bespoke ILog: the .NET convention is that
      libraries accept ILogger<T>, and this package is already in the WPF
      host's restored graph via Microsoft.AspNetCore.Components.WebView.Wpf,
      so referencing it here adds nothing to the published artifact.
    -->
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.10" />
```

- [ ] **Step 2: Verify the package restores**

Run: `dotnet build src/SteamAchievements.Core`
Expected: `0 Error(s)`.

- [ ] **Step 3: Write the failing tests**

Create `tests/SteamAchievements.Core.Tests/Diagnostics/RedactionTests.cs`:

```csharp
using SteamAchievements.Core.Diagnostics;

namespace SteamAchievements.Core.Tests.Diagnostics;

public class RedactionTests
{
    [Fact]
    public void StripsTheKeyFromAQueryStringButKeepsEverythingElse()
    {
        const string url =
            "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/"
            + "?key=ABCDEF0123456789ABCDEF0123456789&steamid=76561198000000000&format=json";

        Assert.Equal(
            "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/"
            + "?key=***&steamid=76561198000000000&format=json",
            Redaction.Scrub(url));
    }

    [Fact]
    public void StripsAnAccessTokenAndKeepsItsParameterName()
    {
        Assert.Equal(
            "GET /x?access_token=***&b=2",
            Redaction.Scrub("GET /x?access_token=9f8e7d6c5b4a&b=2"));
    }

    [Fact]
    public void MatchesTheParameterNameCaseInsensitively()
    {
        Assert.Equal("?KEY=***", Redaction.Scrub("?KEY=ABCDEF0123456789ABCDEF0123456789"));
    }

    [Fact]
    public void LeavesAParameterThatMerelyEndsInKeyAlone()
    {
        Assert.Equal("?monkey=banana", Redaction.Scrub("?monkey=banana"));
    }

    [Fact]
    public void StripsABareTokenShapedLikeAnApiKey()
    {
        Assert.Equal(
            "Steam rejected ***",
            Redaction.Scrub("Steam rejected ABCDEF0123456789ABCDEF0123456789"));
    }

    [Fact]
    public void LeavesAFortyCharacterLowercaseHashAlone()
    {
        // Achievement icon URLs carry SHA-1 hashes. Scrubbing those would empty
        // the log of the URLs it exists to record.
        const string icon = "https://media.steampowered.com/a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4.jpg";

        Assert.Equal(icon, Redaction.Scrub(icon));
    }

    [Fact]
    public void LeavesAKeyShapedRunThatIsPartOfALongerTokenAlone()
    {
        Assert.Equal(
            "XABCDEF0123456789ABCDEF0123456789X",
            Redaction.Scrub("XABCDEF0123456789ABCDEF0123456789X"));
    }

    [Fact]
    public void PassesAnOrdinaryMessageThroughUnchanged()
    {
        Assert.Equal("sync started force=False", Redaction.Scrub("sync started force=False"));
    }

    [Fact]
    public void HandlesAnEmptyString()
    {
        Assert.Equal("", Redaction.Scrub(""));
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/SteamAchievements.Core.Tests --filter FullyQualifiedName~RedactionTests`
Expected: build failure — `Redaction` does not exist.

- [ ] **Step 5: Write the implementation**

Create `src/SteamAchievements.Core/Diagnostics/Redaction.cs`:

```csharp
using System.Text.RegularExpressions;

namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// Removes anything secret from a line on its way to the log.
///
/// Applied by the writer rather than at the call sites, because a scrubber you
/// have to remember to call is a scrubber that leaks: Steam's request URLs
/// carry the API key in their query string, and <c>SteamApiException</c>
/// messages carry those URLs, so the secret can reach a log line through paths
/// nobody inspected.
///
/// Both rules are format-based on purpose — the writer is never handed the
/// secret to compare against. Neither can be complete, so the call-site rule
/// stands alongside them: nothing ever passes an API key to a logger.
/// </summary>
public static partial class Redaction
{
    private const string Mask = "***";

    /// <summary>
    /// The parameter name is kept and only the value is masked, so a log line
    /// still shows that a key was sent.
    /// </summary>
    [GeneratedRegex(@"(?<name>\b(?:key|access_token)=)[^&\s]*", RegexOptions.IgnoreCase)]
    private static partial Regex QueryParameter();

    /// <summary>
    /// The shape of a Steam Web API key: exactly 32 uppercase hex characters,
    /// standing alone. Achievement icon URLs carry 40-character lowercase
    /// SHA-1 hashes, which this deliberately does not match.
    /// </summary>
    [GeneratedRegex(@"(?<![0-9A-Za-z])[0-9A-F]{32}(?![0-9A-Za-z])")]
    private static partial Regex ApiKeyShaped();

    public static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var scrubbed = QueryParameter().Replace(text, match => match.Groups["name"].Value + Mask);

        return ApiKeyShaped().Replace(scrubbed, Mask);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/SteamAchievements.Core.Tests --filter FullyQualifiedName~RedactionTests`
Expected: all 9 pass.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test tests/SteamAchievements.Core.Tests`
Expected: 325 passed, 0 failed.

- [ ] **Step 8: Format and commit**

```bash
dotnet format src/SteamAchievements.Core
dotnet format tests/SteamAchievements.Core.Tests
git add src/SteamAchievements.Core tests/SteamAchievements.Core.Tests
git commit -m "feat: strip secrets from anything on its way to a log line"
```

---

### Task 2: `LogLine`

**Files:**
- Create: `src/SteamAchievements.Core/Diagnostics/LogLine.cs`
- Test: `tests/SteamAchievements.Core.Tests/Diagnostics/LogLineTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `LogLine.Format(DateTimeOffset at, LogLevel level, string category, string message, Exception? error) -> string` (the returned string ends with `\r\n`) and `LogLine.ShortCategory(string category) -> string`. Task 4 calls both.

- [ ] **Step 1: Write the failing tests**

Create `tests/SteamAchievements.Core.Tests/Diagnostics/LogLineTests.cs`:

```csharp
using Microsoft.Extensions.Logging;
using SteamAchievements.Core.Diagnostics;

namespace SteamAchievements.Core.Tests.Diagnostics;

public class LogLineTests
{
    private static readonly DateTimeOffset At =
        new(2026, 7, 27, 9, 14, 2, 113, TimeSpan.Zero);

    [Fact]
    public void WritesASortableUtcTimestampALevelACategoryAndTheMessage()
    {
        Assert.Equal(
            "2026-07-27 09:14:02.113Z  DBG  SyncCoordinator  sync started force=False\r\n",
            LogLine.Format(At, LogLevel.Debug, "SteamAchievements.Core.App.SyncCoordinator",
                "sync started force=False", null));
    }

    [Fact]
    public void ConvertsANonUtcTimestampRatherThanPrintingItsLocalFace()
    {
        // Two hours ahead of the same instant above.
        var offset = new DateTimeOffset(2026, 7, 27, 11, 14, 2, 113, TimeSpan.FromHours(2));

        Assert.StartsWith("2026-07-27 09:14:02.113Z", LogLine.Format(offset, LogLevel.Debug, "X", "m", null));
    }

    [Theory]
    [InlineData(LogLevel.Trace, "TRC")]
    [InlineData(LogLevel.Debug, "DBG")]
    [InlineData(LogLevel.Information, "INF")]
    [InlineData(LogLevel.Warning, "WRN")]
    [InlineData(LogLevel.Error, "ERR")]
    [InlineData(LogLevel.Critical, "CRT")]
    public void AbbreviatesEveryLevelToThreeCharacters(LogLevel level, string expected)
    {
        Assert.Equal($"2026-07-27 09:14:02.113Z  {expected}  X  m\r\n",
            LogLine.Format(At, level, "X", "m", null));
    }

    [Fact]
    public void AppendsAnExceptionAsAnIndentedBlockAfterItsLine()
    {
        Exception caught;

        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception e)
        {
            caught = e;
        }

        var formatted = LogLine.Format(At, LogLevel.Error, "X", "it failed", caught);
        var lines = formatted.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("2026-07-27 09:14:02.113Z  ERR  X  it failed", lines[0]);
        Assert.StartsWith("    System.InvalidOperationException: boom", lines[1]);
        Assert.All(lines.Skip(1), line => Assert.StartsWith("    ", line));
        Assert.EndsWith("\r\n", formatted);
    }

    [Fact]
    public void ShortensANamespacedCategoryToItsLastSegment()
    {
        Assert.Equal("SyncCoordinator", LogLine.ShortCategory("SteamAchievements.Core.App.SyncCoordinator"));
    }

    [Fact]
    public void LeavesACategoryWithNoNamespaceAlone()
    {
        Assert.Equal("Program", LogLine.ShortCategory("Program"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SteamAchievements.Core.Tests --filter FullyQualifiedName~LogLineTests`
Expected: build failure — `LogLine` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/SteamAchievements.Core/Diagnostics/LogLine.cs`:

```csharp
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// Turns one logged event into the text that goes in the file. Pure, so the
/// tests can assert exact output rather than a shape.
/// </summary>
public static class LogLine
{
    /// <summary>
    /// Hardcoded rather than <see cref="Environment.NewLine"/>. The file is
    /// read on Windows, where tools still expect CRLF, and the tests run on
    /// macOS and Linux — an ambient newline would make the expected output
    /// depend on the host.
    /// </summary>
    private const string NewLine = "\r\n";

    private const string Indent = "    ";

    public static string Format(
        DateTimeOffset at, LogLevel level, string category, string message, Exception? error)
    {
        var builder = new StringBuilder();

        builder.Append(at.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        builder.Append("Z  ");
        builder.Append(Abbreviate(level));
        builder.Append("  ");
        builder.Append(ShortCategory(category));
        builder.Append("  ");
        builder.Append(message);
        builder.Append(NewLine);

        if (error is not null)
        {
            // Indented so the block is visibly subordinate to its line, and so
            // a search for a category still finds the event rather than a
            // stack frame that happens to mention it.
            foreach (var line in error.ToString().Split('\n'))
            {
                builder.Append(Indent).Append(line.TrimEnd('\r')).Append(NewLine);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// <c>SteamAchievements.Core.App.SyncCoordinator</c> becomes
    /// <c>SyncCoordinator</c>. The namespace is noise once every line carries
    /// it.
    /// </summary>
    public static string ShortCategory(string category)
    {
        var lastDot = category.LastIndexOf('.');

        return lastDot < 0 ? category : category[(lastDot + 1)..];
    }

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/SteamAchievements.Core.Tests --filter FullyQualifiedName~LogLineTests`
Expected: all pass.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test tests/SteamAchievements.Core.Tests`
Expected: 336 passed, 0 failed.

- [ ] **Step 6: Format and commit**

```bash
dotnet format src/SteamAchievements.Core
dotnet format tests/SteamAchievements.Core.Tests
git add src/SteamAchievements.Core tests/SteamAchievements.Core.Tests
git commit -m "feat: give a logged event one greppable line"
```

---

### Task 3: `LogFileOptions` and `RollingFileWriter`

**Files:**
- Create: `src/SteamAchievements.Core/Diagnostics/LogFileOptions.cs`
- Create: `src/SteamAchievements.Core/Diagnostics/RollingFileWriter.cs`
- Test: `tests/SteamAchievements.Core.Tests/Diagnostics/RollingFileWriterTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public sealed record LogFileOptions(string Directory, string FileName = "log.txt", long MaxBytes = 2 * 1024 * 1024, int MaxFiles = 4)` and `internal sealed class RollingFileWriter : IDisposable` with `void Write(string text)` and `bool Disabled { get; }`. Task 4 constructs the writer.

`RollingFileWriter` is `internal`; `SteamAchievements.Core` already grants `InternalsVisibleTo("SteamAchievements.Core.Tests")`, so the tests reach it without making it public.

- [ ] **Step 1: Write the failing tests**

Create `tests/SteamAchievements.Core.Tests/Diagnostics/RollingFileWriterTests.cs`:

```csharp
using SteamAchievements.Core.Diagnostics;

namespace SteamAchievements.Core.Tests.Diagnostics;

public class RollingFileWriterTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "satlog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string Current => Path.Combine(_directory, "log.txt");

    private string Rotated(int index) => Path.Combine(_directory, $"log.{index}.txt");

    [Fact]
    public void CreatesTheDirectoryAndTheFileOnTheFirstWrite()
    {
        using var writer = new RollingFileWriter(new LogFileOptions(_directory));

        writer.Write("first\r\n");

        Assert.Equal("first\r\n", File.ReadAllText(Current));
    }

    [Fact]
    public void AppendsRatherThanTruncating()
    {
        using var writer = new RollingFileWriter(new LogFileOptions(_directory));

        writer.Write("one\r\n");
        writer.Write("two\r\n");

        Assert.Equal("one\r\ntwo\r\n", File.ReadAllText(Current));
    }

    [Fact]
    public void LeavesTheFileReadableWhileItIsStillOpen()
    {
        // "Open log" shows the file while the application is running, so the
        // writer must not hold an exclusive handle.
        using var writer = new RollingFileWriter(new LogFileOptions(_directory));

        writer.Write("live\r\n");

        using var reader = new FileStream(Current, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var text = new StreamReader(reader);
        Assert.Equal("live\r\n", text.ReadToEnd());
    }

    [Fact]
    public void RotatesWhenTheNextWriteWouldPassTheLimit()
    {
        using var writer = new RollingFileWriter(new LogFileOptions(_directory, MaxBytes: 10));

        writer.Write("123456\r\n");  // 8 bytes, fits
        writer.Write("789012\r\n");  // would reach 16, so rotate first

        Assert.Equal("123456\r\n", File.ReadAllText(Rotated(1)));
        Assert.Equal("789012\r\n", File.ReadAllText(Current));
    }

    [Fact]
    public void KeepsTheNewestLinesInTheCurrentFile()
    {
        using var writer = new RollingFileWriter(new LogFileOptions(_directory, MaxBytes: 10));

        writer.Write("oldest\r\n");
        writer.Write("newest\r\n");

        Assert.Contains("newest", File.ReadAllText(Current));
        Assert.DoesNotContain("newest", File.ReadAllText(Rotated(1)));
    }

    [Fact]
    public void KeepsAtMostMaxFilesAndDiscardsTheOldest()
    {
        using (var writer = new RollingFileWriter(new LogFileOptions(_directory, MaxBytes: 10, MaxFiles: 4)))
        {
            foreach (var n in Enumerable.Range(1, 8))
            {
                writer.Write($"line-{n}\r\n");
            }
        }

        Assert.Equal(4, Directory.GetFiles(_directory).Length);
        Assert.True(File.Exists(Current));
        Assert.True(File.Exists(Rotated(1)));
        Assert.True(File.Exists(Rotated(2)));
        Assert.True(File.Exists(Rotated(3)));
        Assert.False(File.Exists(Rotated(4)));
    }

    [Fact]
    public void WritesASingleLineLargerThanTheLimitRatherThanRotatingForever()
    {
        using var writer = new RollingFileWriter(new LogFileOptions(_directory, MaxBytes: 4));

        writer.Write("a line much longer than four bytes\r\n");

        Assert.Contains("much longer", File.ReadAllText(Current));
        Assert.False(File.Exists(Rotated(1)));
    }

    [Fact]
    public void DisablesItselfPermanentlyWhenTheFileCannotBeOpened()
    {
        // A file where the directory should be: opening anything inside it
        // fails, and no amount of retrying will change that.
        var blocker = Path.Combine(Path.GetTempPath(), "satlog-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "not a directory");

        try
        {
            using var writer = new RollingFileWriter(new LogFileOptions(blocker));

            writer.Write("never lands\r\n");

            Assert.True(writer.Disabled);

            // The second write must not throw either.
            writer.Write("still nothing\r\n");
            Assert.True(writer.Disabled);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public void WritesFromManyThreadsWithoutLosingOrInterleavingALine()
    {
        const int threads = 8;
        const int perThread = 200;

        using (var writer = new RollingFileWriter(new LogFileOptions(_directory)))
        {
            Parallel.For(0, threads, thread =>
            {
                foreach (var n in Enumerable.Range(0, perThread))
                {
                    writer.Write($"thread-{thread}-line-{n}\r\n");
                }
            });
        }

        var lines = File.ReadAllLines(Current);

        Assert.Equal(threads * perThread, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"^thread-\d-line-\d+$", line));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SteamAchievements.Core.Tests --filter FullyQualifiedName~RollingFileWriterTests`
Expected: build failure — `LogFileOptions` and `RollingFileWriter` do not exist.

- [ ] **Step 3: Write `LogFileOptions`**

Create `src/SteamAchievements.Core/Diagnostics/LogFileOptions.cs`:

```csharp
namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// Where the log goes and how much of it is kept.
///
/// <paramref name="MaxFiles"/> counts the current file, so the default of four
/// means <c>log.txt</c> plus <c>log.1.txt</c> through <c>log.3.txt</c> — eight
/// megabytes in total, which is several full syncs of history and still small
/// enough to attach to an issue.
/// </summary>
public sealed record LogFileOptions(
    string Directory,
    string FileName = "log.txt",
    long MaxBytes = 2 * 1024 * 1024,
    int MaxFiles = 4);
```

- [ ] **Step 4: Write `RollingFileWriter`**

Create `src/SteamAchievements.Core/Diagnostics/RollingFileWriter.cs`:

```csharp
using System.Text;

namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// The one stateful part of the log: an append-only file that rotates by size.
///
/// Synchronous, under a single lock, flushed on every write. A queue with a
/// background writer would be faster and would also lose exactly what this
/// exists to keep — the lines still in memory when the process dies. At
/// roughly six thousand lines per full sync there is no throughput problem to
/// solve.
/// </summary>
internal sealed class RollingFileWriter : IDisposable
{
    private readonly LogFileOptions _options;
    private readonly string _currentPath;
    private readonly string _stem;
    private readonly string _extension;
    private readonly Lock _gate = new();

    private FileStream? _stream;
    private long _length;

    public RollingFileWriter(LogFileOptions options)
    {
        _options = options;
        _currentPath = Path.Combine(options.Directory, options.FileName);
        _stem = Path.GetFileNameWithoutExtension(options.FileName);
        _extension = Path.GetExtension(options.FileName);
    }

    /// <summary>
    /// Set once the file has proved unwritable, and never cleared. Logging must
    /// never be the reason the application fails to start, and a writer that
    /// retried on every line would turn a permissions problem into a
    /// nine-minute stall during a sync.
    /// </summary>
    public bool Disabled { get; private set; }

    public void Write(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        lock (_gate)
        {
            if (Disabled)
            {
                return;
            }

            try
            {
                Open();

                // The _length > 0 test is what stops a line larger than the
                // whole budget from rotating on every write and filling the
                // folder with empty files.
                if (_length > 0 && _length + bytes.Length > _options.MaxBytes)
                {
                    Rotate();
                    Open();
                }

                _stream!.Write(bytes);
                _stream.Flush();
                _length += bytes.Length;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Disabled = true;
                Close();
            }
        }
    }

    private void Open()
    {
        if (_stream is not null)
        {
            return;
        }

        Directory.CreateDirectory(_options.Directory);

        // FileShare.ReadWrite so "Open log" can show the file while the
        // application is still writing to it.
        _stream = new FileStream(_currentPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _length = _stream.Length;
    }

    private void Rotate()
    {
        Close();

        var oldest = Path.Combine(_options.Directory, $"{_stem}.{_options.MaxFiles - 1}{_extension}");

        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = _options.MaxFiles - 2; index >= 1; index--)
        {
            var from = Path.Combine(_options.Directory, $"{_stem}.{index}{_extension}");

            if (File.Exists(from))
            {
                File.Move(from, Path.Combine(_options.Directory, $"{_stem}.{index + 1}{_extension}"));
            }
        }

        if (File.Exists(_currentPath))
        {
            File.Move(_currentPath, Path.Combine(_options.Directory, $"{_stem}.1{_extension}"));
        }

        _length = 0;
    }

    private void Close()
    {
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Close();
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/SteamAchievements.Core.Tests --filter FullyQualifiedName~RollingFileWriterTests`
Expected: all 9 pass.

If `Disables_itself_permanently_when_the_file_cannot_be_opened` fails because `Directory.CreateDirectory` over an existing file throws something other than `IOException` on this host, widen the `catch` filter to include the type actually thrown and record it in §10 of the design document. Do not weaken the assertion.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test tests/SteamAchievements.Core.Tests`
Expected: 345 passed, 0 failed.

- [ ] **Step 7: Format and commit**

```bash
dotnet format src/SteamAchievements.Core
dotnet format tests/SteamAchievements.Core.Tests
git add src/SteamAchievements.Core tests/SteamAchievements.Core.Tests
git commit -m "feat: keep the log bounded without losing the newest lines"
```

---

### Task 4: `RollingFileLoggerProvider`

**Files:**
- Create: `src/SteamAchievements.Core/Diagnostics/RollingFileLoggerProvider.cs`
- Test: `tests/SteamAchievements.Core.Tests/Diagnostics/RollingFileLoggerProviderTests.cs`

**Interfaces:**
- Consumes: `LogFileOptions`, `RollingFileWriter`, `LogLine.Format`, `Redaction.Scrub`.
- Produces: `public sealed class RollingFileLoggerProvider : ILoggerProvider` with constructor `(LogFileOptions options, Func<DateTimeOffset> now)`. Tasks 11 registers it.

- [ ] **Step 1: Write the failing tests**

Create `tests/SteamAchievements.Core.Tests/Diagnostics/RollingFileLoggerProviderTests.cs`:

```csharp
using Microsoft.Extensions.Logging;
using SteamAchievements.Core.Diagnostics;

namespace SteamAchievements.Core.Tests.Diagnostics;

public class RollingFileLoggerProviderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "satprovider-" + Guid.NewGuid().ToString("N"));

    private static readonly DateTimeOffset At =
        new(2026, 7, 27, 9, 14, 2, 113, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string Text => File.ReadAllText(Path.Combine(_directory, "log.txt"));

    private RollingFileLoggerProvider NewProvider() =>
        new(new LogFileOptions(_directory), () => At);

    [Fact]
    public void WritesAFormattedLineForALoggedMessage()
    {
        using var provider = NewProvider();

        provider.CreateLogger("SteamAchievements.Core.App.SyncCoordinator")
            .LogDebug("sync started force={Force}", false);

        Assert.Equal(
            "2026-07-27 09:14:02.113Z  DBG  SyncCoordinator  sync started force=False\r\n",
            Text);
    }

    [Fact]
    public void WritesEveryLevelBecauseNothingIsFiltered()
    {
        using var provider = NewProvider();
        var log = provider.CreateLogger("X");

        log.LogTrace("t");
        log.LogDebug("d");
        log.LogInformation("i");
        log.LogWarning("w");
        log.LogError("e");
        log.LogCritical("c");

        Assert.Equal(6, Text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void ReportsEveryLevelAsEnabledExceptNone()
    {
        using var provider = NewProvider();
        var log = provider.CreateLogger("X");

        Assert.True(log.IsEnabled(LogLevel.Trace));
        Assert.True(log.IsEnabled(LogLevel.Critical));
        Assert.False(log.IsEnabled(LogLevel.None));
    }

    [Fact]
    public void ScrubsASecretThatReachesTheLineThroughAMessage()
    {
        using var provider = NewProvider();

        provider.CreateLogger("X").LogDebug(
            "GET https://api.steampowered.com/x?key=ABCDEF0123456789ABCDEF0123456789&steamid=7");

        Assert.DoesNotContain("ABCDEF0123456789ABCDEF0123456789", Text);
        Assert.Contains("key=***", Text);
    }

    [Fact]
    public void ScrubsASecretThatReachesTheLineThroughAnException()
    {
        using var provider = NewProvider();

        provider.CreateLogger("X").LogError(
            new InvalidOperationException("failed calling ?key=ABCDEF0123456789ABCDEF0123456789"),
            "request failed");

        Assert.DoesNotContain("ABCDEF0123456789ABCDEF0123456789", Text);
    }

    [Fact]
    public void ReturnsAScopeThatCanBeDisposedWithoutDoingAnything()
    {
        using var provider = NewProvider();

        using (provider.CreateLogger("X").BeginScope("ignored"))
        {
            provider.CreateLogger("X").LogInformation("inside");
        }

        Assert.Contains("inside", Text);
    }

    [Fact]
    public void SharesOneFileAcrossEveryCategory()
    {
        using var provider = NewProvider();

        provider.CreateLogger("A").LogInformation("from a");
        provider.CreateLogger("B").LogInformation("from b");

        Assert.Contains("A  from a", Text);
        Assert.Contains("B  from b", Text);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SteamAchievements.Core.Tests --filter FullyQualifiedName~RollingFileLoggerProviderTests`
Expected: build failure — `RollingFileLoggerProvider` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/SteamAchievements.Core/Diagnostics/RollingFileLoggerProvider.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// The file sink, as an <see cref="ILoggerProvider"/> so that call sites can
/// take the conventional <c>ILogger&lt;T&gt;</c>.
///
/// Nothing is filtered: the application has never started on Windows, and the
/// first failure has to be in the file already rather than reproducible on a
/// second run with a flag set. Raising the floor later is a one-line change in
/// the host.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly RollingFileWriter _writer;
    private readonly Func<DateTimeOffset> _now;

    /// <param name="now">
    /// Injected rather than read ambiently, which is the convention everywhere
    /// else in Core and is what lets the formatting tests assert exact output.
    /// </param>
    public RollingFileLoggerProvider(LogFileOptions options, Func<DateTimeOffset> now)
    {
        _writer = new RollingFileWriter(options);
        _now = now;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <summary>
    /// Redaction happens here, on the whole formatted line including the
    /// exception block, rather than at the call sites — a scrubber you have to
    /// remember to call is a scrubber that leaks.
    /// </summary>
    private void Write(LogLevel level, string category, string message, Exception? error) =>
        _writer.Write(Redaction.Scrub(LogLine.Format(_now(), level, category, message, error)));

    public void Dispose() => _writer.Dispose();

    private sealed class FileLogger : ILogger
    {
        private readonly RollingFileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(RollingFileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        /// <summary>
        /// Scopes are not supported. Nothing in this application nests work in
        /// a way a scope would clarify, and a no-op is honest about that.
        /// </summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            _provider.Write(logLevel, _category, formatter(state, exception), exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        private NullScope()
        {
        }

        public void Dispose()
        {
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/SteamAchievements.Core.Tests --filter FullyQualifiedName~RollingFileLoggerProviderTests`
Expected: all 7 pass.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test tests/SteamAchievements.Core.Tests`
Expected: 352 passed, 0 failed.

- [ ] **Step 6: Format and commit**

```bash
dotnet format src/SteamAchievements.Core
dotnet format tests/SteamAchievements.Core.Tests
git add src/SteamAchievements.Core tests/SteamAchievements.Core.Tests
git commit -m "feat: put the log behind the conventional ILogger seam"
```

---

### Task 5: `LoggingHandler`

**Files:**
- Create: `src/SteamAchievements.Core/Diagnostics/LoggingHandler.cs`
- Test: `tests/SteamAchievements.Core.Tests/Diagnostics/LoggingHandlerTests.cs`

**Interfaces:**
- Consumes: `RollingFileLoggerProvider` (for the end-to-end test only).
- Produces: `public sealed class LoggingHandler : DelegatingHandler` with constructor `(ILogger<LoggingHandler> log)`. Tasks 10 and 11 insert it into the `HttpClient` chain.

- [ ] **Step 1: Write the failing tests**

Create `tests/SteamAchievements.Core.Tests/Diagnostics/LoggingHandlerTests.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SteamAchievements.Core.Diagnostics;

namespace SteamAchievements.Core.Tests.Diagnostics;

public class LoggingHandlerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "sathandler-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;

        public StubHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond());
    }

    private static HttpClient ClientOver(ILogger<LoggingHandler> log, Func<HttpResponseMessage> respond) =>
        new(new LoggingHandler(log) { InnerHandler = new StubHandler(respond) });

    [Fact]
    public async Task LogsTheMethodTheUrlAndTheStatus()
    {
        var provider = new RollingFileLoggerProvider(
            new LogFileOptions(_directory), () => DateTimeOffset.UnixEpoch);
        var log = new LoggerFactory([provider]).CreateLogger<LoggingHandler>();

        using var client = ClientOver(log, () => new HttpResponseMessage(HttpStatusCode.OK));
        await client.GetAsync("https://api.steampowered.com/x?steamid=7");

        provider.Dispose();
        var text = File.ReadAllText(Path.Combine(_directory, "log.txt"));

        Assert.Contains("GET", text);
        Assert.Contains("https://api.steampowered.com/x?steamid=7", text);
        Assert.Contains("200", text);
    }

    [Fact]
    public async Task NeverLetsTheKeyInAUrlReachTheFile()
    {
        var provider = new RollingFileLoggerProvider(
            new LogFileOptions(_directory), () => DateTimeOffset.UnixEpoch);
        var log = new LoggerFactory([provider]).CreateLogger<LoggingHandler>();

        using var client = ClientOver(log, () => new HttpResponseMessage(HttpStatusCode.OK));
        await client.GetAsync(
            "https://api.steampowered.com/x?key=ABCDEF0123456789ABCDEF0123456789&steamid=7");

        provider.Dispose();
        var text = File.ReadAllText(Path.Combine(_directory, "log.txt"));

        Assert.DoesNotContain("ABCDEF0123456789ABCDEF0123456789", text);
        Assert.Contains("key=***", text);
        Assert.Contains("steamid=7", text);
    }

    [Fact]
    public async Task LogsAFailingStatusWithoutTreatingItAsAnError()
    {
        var provider = new RollingFileLoggerProvider(
            new LogFileOptions(_directory), () => DateTimeOffset.UnixEpoch);
        var log = new LoggerFactory([provider]).CreateLogger<LoggingHandler>();

        using var client = ClientOver(log, () => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await client.GetAsync("https://api.steampowered.com/x");

        provider.Dispose();

        Assert.Contains("401", File.ReadAllText(Path.Combine(_directory, "log.txt")));
    }

    [Fact]
    public async Task RethrowsATransportFailureAfterLoggingIt()
    {
        var provider = new RollingFileLoggerProvider(
            new LogFileOptions(_directory), () => DateTimeOffset.UnixEpoch);
        var log = new LoggerFactory([provider]).CreateLogger<LoggingHandler>();

        using var client = ClientOver(log, () => throw new HttpRequestException("no route to host"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("https://api.steampowered.com/x"));

        provider.Dispose();
        var text = File.ReadAllText(Path.Combine(_directory, "log.txt"));

        Assert.Contains("ERR", text);
        Assert.Contains("no route to host", text);
    }

    [Fact]
    public async Task WorksWithANullLogger()
    {
        using var client = ClientOver(
            NullLogger<LoggingHandler>.Instance, () => new HttpResponseMessage(HttpStatusCode.OK));

        var response = await client.GetAsync("https://api.steampowered.com/x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SteamAchievements.Core.Tests --filter FullyQualifiedName~LoggingHandlerTests`
Expected: build failure — `LoggingHandler` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/SteamAchievements.Core/Diagnostics/LoggingHandler.cs`:

```csharp
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// Records every Steam request: method, URL, status and how long it took.
///
/// A <see cref="DelegatingHandler"/> rather than a change to
/// <c>SteamApiClient</c>. The client has no reason to know its traffic is
/// observed, the CLI already composes handlers this way, and a stub inner
/// handler makes this testable without a network.
///
/// The URL carries the API key in its query string. Nothing is stripped here
/// on purpose — <see cref="Redaction"/> runs inside the writer, so a URL
/// logged from anywhere else is covered by the same rule rather than by a
/// second copy of it that can drift.
/// </summary>
public sealed class LoggingHandler : DelegatingHandler
{
    private readonly ILogger<LoggingHandler> _log;

    public LoggingHandler(ILogger<LoggingHandler> log) => _log = log;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var url = request.RequestUri?.ToString() ?? "(no uri)";

        try
        {
            var response = await base.SendAsync(request, cancellationToken);

            // A 401 or a 429 is data, not a failure of this handler: the error
            // taxonomy in SteamApiClient decides what it means.
            _log.LogDebug(
                "{Method} {Url} -> {Status} in {Elapsed}ms",
                request.Method.Method, url, (int)response.StatusCode, Elapsed(started));

            return response;
        }
        catch (Exception e)
        {
            _log.LogError(
                e, "{Method} {Url} -> failed after {Elapsed}ms", request.Method.Method, url, Elapsed(started));
            throw;
        }
    }

    private static long Elapsed(long started) =>
        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/SteamAchievements.Core.Tests --filter FullyQualifiedName~LoggingHandlerTests`
Expected: all 5 pass.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test tests/SteamAchievements.Core.Tests`
Expected: 357 passed, 0 failed.

- [ ] **Step 6: Format and commit**

```bash
dotnet format src/SteamAchievements.Core
dotnet format tests/SteamAchievements.Core.Tests
git add src/SteamAchievements.Core tests/SteamAchievements.Core.Tests
git commit -m "feat: record every Steam request without recording the key"
```

---

### Task 6: A recording logger for the tests

**Files:**
- Modify: `tests/SteamAchievements.Core.Tests/Fakes.cs`
- Test: `tests/SteamAchievements.Core.Tests/Diagnostics/RecordingLoggerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `RecordingLogger<T> : ILogger<T>` with `IReadOnlyList<string> Lines { get; }`, `IReadOnlyList<Exception?> Errors { get; }` and `bool Logged(string fragment)`. Tasks 7, 8 and 9 assert with it.

Read `tests/SteamAchievements.Core.Tests/Fakes.cs` before editing and follow the file's existing style. Append the new type; do not restructure what is there.

- [ ] **Step 1: Write the failing test**

Create `tests/SteamAchievements.Core.Tests/Diagnostics/RecordingLoggerTests.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace SteamAchievements.Core.Tests.Diagnostics;

public class RecordingLoggerTests
{
    [Fact]
    public void RecordsTheFormattedMessageOfEveryCall()
    {
        var log = new RecordingLogger<RecordingLoggerTests>();

        log.LogInformation("plan has {Count} games", 1483);

        Assert.Equal(["plan has 1483 games"], log.Lines);
    }

    [Fact]
    public void RecordsTheExceptionAlongsideTheMessage()
    {
        var log = new RecordingLogger<RecordingLoggerTests>();
        var boom = new InvalidOperationException("boom");

        log.LogError(boom, "it failed");

        Assert.Same(boom, log.Errors.Single());
    }

    [Fact]
    public void AnswersWhetherAFragmentWasLogged()
    {
        var log = new RecordingLogger<RecordingLoggerTests>();

        log.LogDebug("sync completed in 512ms");

        Assert.True(log.Logged("sync completed"));
        Assert.False(log.Logged("sync failed"));
    }

    [Fact]
    public void IsSafeToWriteToFromSeveralThreads()
    {
        // SyncOrchestrator logs from four worker threads.
        var log = new RecordingLogger<RecordingLoggerTests>();

        Parallel.For(0, 200, n => log.LogDebug("line {N}", n));

        Assert.Equal(200, log.Lines.Count);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SteamAchievements.Core.Tests --filter FullyQualifiedName~RecordingLoggerTests`
Expected: build failure — `RecordingLogger` does not exist.

- [ ] **Step 3: Append the implementation to `Fakes.cs`**

Add to `tests/SteamAchievements.Core.Tests/Fakes.cs`, keeping the existing `using` directives and adding `using Microsoft.Extensions.Logging;` if it is not already there:

```csharp
/// <summary>
/// Captures what a component logged, so a test can assert that a branch which
/// otherwise leaves no trace — a swallowed exception, a cancelled run — said so.
///
/// The list is guarded because SyncOrchestrator logs from four worker threads
/// and an unsynchronized List would drop entries under exactly the load these
/// tests exist to cover.
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly Lock _gate = new();
    private readonly List<string> _lines = [];
    private readonly List<Exception?> _errors = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate)
            {
                return _lines.ToArray();
            }
        }
    }

    public IReadOnlyList<Exception?> Errors
    {
        get
        {
            lock (_gate)
            {
                return _errors.ToArray();
            }
        }
    }

    public bool Logged(string fragment) => Lines.Any(line => line.Contains(fragment, StringComparison.Ordinal));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
        {
            _lines.Add(formatter(state, exception));

            if (exception is not null)
            {
                _errors.Add(exception);
            }
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/SteamAchievements.Core.Tests --filter FullyQualifiedName~RecordingLoggerTests`
Expected: all 4 pass.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test tests/SteamAchievements.Core.Tests`
Expected: 361 passed, 0 failed.

- [ ] **Step 6: Format and commit**

```bash
dotnet format tests/SteamAchievements.Core.Tests
git add tests/SteamAchievements.Core.Tests
git commit -m "test: let a test assert what a component logged"
```

---

### Task 7: The sync path

**Files:**
- Modify: `src/SteamAchievements.Core/App/SyncCoordinator.cs`
- Modify: `src/SteamAchievements.Core/Sync/LiveSyncRunner.cs`
- Modify: `src/SteamAchievements.Core/Sync/SyncOrchestrator.cs`
- Modify: `tests/SteamAchievements.Core.Tests/App/SyncCoordinatorTests.cs` (2 construction sites)
- Modify: `tests/SteamAchievements.Core.Tests/Sync/SyncOrchestratorTests.cs` (6 construction sites)
- Modify: `src/SteamAchievements.Cli/Program.cs` (1 construction site)

**Interfaces:**
- Consumes: `RecordingLogger<T>` from Task 6.
- Produces:
  - `SyncCoordinator(ISyncRunner runner, IAccountStore accounts, SyncJournal journal, Func<DateTimeOffset> now, ILogger<SyncCoordinator> log)` — the logger is the **last** parameter.
  - `LiveSyncRunner(ISecretStore secrets, GameRepository repository, Func<string, SteamApiClient> clientFactory, ILoggerFactory loggers)` — an `ILoggerFactory`, not a logger, because it builds a `SyncOrchestrator` per run.
  - `SyncOrchestrator(SteamApiClient client, GameRepository repository, SyncOptions options, ILogger<SyncOrchestrator> log, TimeSpan? retryBaseDelay = null)` — the logger goes **before** the existing optional `retryBaseDelay`, which must stay optional and stay last.

- [ ] **Step 1: Widen the two test helpers**

Both files build their subject through a private static helper. Extend the helper rather than writing a second one.

In `tests/SteamAchievements.Core.Tests/App/SyncCoordinatorTests.cs`, add `using Microsoft.Extensions.Logging;` and `using Microsoft.Extensions.Logging.Abstractions;`, then change `Build`'s signature and its return line:

```csharp
    private static (SyncCoordinator Coordinator, Microsoft.Data.Sqlite.SqliteConnection Connection, IAccountStore Accounts)
        Build(ISyncRunner runner, bool withAccount = true, ILogger<SyncCoordinator>? log = null)
```

```csharp
        return (
            new SyncCoordinator(
                runner, accounts, new SyncJournal(connection), clock.Read,
                log ?? NullLogger<SyncCoordinator>.Instance),
            connection, accounts);
```

An optional parameter is fine *here*: the ban on optional loggers is about production constructors, where a default hides a host that forgot to wire logging up. A test helper has no host.

In `tests/SteamAchievements.Core.Tests/Sync/SyncOrchestratorTests.cs`, add the same two `using` directives and change `Build`'s signature and its return line:

```csharp
    private static async Task<(SyncOrchestrator Sync, GameRepository Repo, FakeHttpMessageHandler Handler)>
        Build(ILogger<SyncOrchestrator>? log = null)
```

```csharp
        return (
            new SyncOrchestrator(
                client, repository, SyncOptions.Default, log ?? NullLogger<SyncOrchestrator>.Instance),
            repository, handler);
```

- [ ] **Step 2: Write the failing tests**

Append to `SyncCoordinatorTests`:

```csharp
    [Fact]
    public void LogsTheStartAndTheCompletionOfASuccessfulRun()
    {
        var log = new RecordingLogger<SyncCoordinator>();
        var (coordinator, connection, _) = Build(
            new FakeSyncRunner((_, _) => Task.CompletedTask), log: log);

        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            coordinator.Completion.Wait();
        }

        Assert.True(log.Logged("sync started"));
        Assert.True(log.Logged("sync completed"));
    }

    [Fact]
    public void LogsAFailedRunWithItsException()
    {
        var log = new RecordingLogger<SyncCoordinator>();
        var (coordinator, connection, _) = Build(
            new FakeSyncRunner((_, _) => Task.FromException(new InvalidOperationException("boom"))),
            log: log);

        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            coordinator.Completion.Wait();
        }

        Assert.True(log.Logged("sync failed"));
        Assert.Contains(log.Errors, e => e?.Message == "boom");
    }

    [Fact]
    public void LogsAPauseAsAPauseRatherThanACancellation()
    {
        var log = new RecordingLogger<SyncCoordinator>();
        var (coordinator, connection, _) = Build(
            new FakeSyncRunner((_, token) => UntilCancelled(token)), log: log);

        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            coordinator.Pause();
            coordinator.Completion.Wait(TimeSpan.FromSeconds(5));
        }

        Assert.True(log.Logged("sync pause requested"));
        Assert.True(log.Logged("sync paused"));
    }

    [Fact]
    public void NeverMentionsAKeyBecauseItNeverReadsOne()
    {
        var log = new RecordingLogger<SyncCoordinator>();
        var (coordinator, connection, _) = Build(
            new FakeSyncRunner((_, _) => Task.CompletedTask), log: log);

        using (connection)
        using (coordinator)
        {
            coordinator.Start(force: false);
            coordinator.Completion.Wait();
        }

        Assert.DoesNotContain(log.Lines, line => line.Contains("key", StringComparison.OrdinalIgnoreCase));
    }
```

Append to `SyncOrchestratorTests`:

```csharp
    [Fact]
    public async Task LogsThePlanSizeAndEachGamesOutcome()
    {
        var log = new RecordingLogger<SyncOrchestrator>();
        var (orchestrator, repository, _) = await Build(log);

        using (repository)
        {
            await orchestrator.RunAsync(SteamId, force: true, null, CancellationToken.None);
        }

        Assert.True(log.Logged("plan:"));
        Assert.Contains(log.Lines, line => line.Contains("synced", StringComparison.Ordinal));

        // appid 220 is the fixture's no-stats game.
        Assert.True(log.Logged("game 220 has no achievements"));
    }
```

If `GameRepository` is not `IDisposable`, drop the `using` block and call `RunAsync` directly, matching whatever the neighbouring tests in that file do with their repository.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/SteamAchievements.Core.Tests --filter FullyQualifiedName~SyncCoordinatorTests`
Expected: build failure — the constructors take no logger.

- [ ] **Step 4: Add the logger to `SyncCoordinator`**

In `src/SteamAchievements.Core/App/SyncCoordinator.cs`, add `using Microsoft.Extensions.Logging;`, add the field and the constructor parameter:

```csharp
    private readonly ILogger<SyncCoordinator> _log;
```

```csharp
    public SyncCoordinator(
        ISyncRunner runner,
        IAccountStore accounts,
        SyncJournal journal,
        Func<DateTimeOffset> now,
        ILogger<SyncCoordinator> log)
    {
        _runner = runner;
        _accounts = accounts;
        _journal = journal;
        _now = now;
        _log = log;
```

Immediately after the existing `_status = _accounts.KeyRejectedAt is not null ? ... : ...;` assignment in the constructor, add:

```csharp
        if (_accounts.KeyRejectedAt is not null)
        {
            _log.LogWarning(
                "starting with a key Steam rejected at {RejectedAt}; syncing is blocked until it is replaced",
                _accounts.KeyRejectedAt);
        }
```

In `Start`, inside the `if (account is null)` branch, before `return`:

```csharp
            _log.LogWarning("sync requested with no Steam account configured");
```

In `Start`, replace `_completion = RunAsync(account.SteamId64, force, _cancellation.Token);` with the same line preceded by:

```csharp
            _log.LogInformation(
                "sync started steam_id={SteamId} force={Force}", account.SteamId64, force);
```

In `Stop`, after `_cancellation?.Cancel();`:

```csharp
            _log.LogInformation("sync {Action} requested", pausing ? "pause" : "cancel");
```

In `RunAsync`, in the success path after `_journal.MarkSyncCompleted(finishedAt);`:

```csharp
            _log.LogInformation(
                "sync completed games={Completed} in {Elapsed}ms",
                completed, (long)(finishedAt - startedAt).TotalMilliseconds);
```

In the `catch (OperationCanceledException)` block, after `Record(...)`:

```csharp
            _log.LogInformation(
                "sync {Outcome} after {Completed} of {Total} games",
                _pausing ? "paused" : "cancelled", completed, total);
```

In the `catch (SteamApiException e) when (e.Kind == SteamApiErrorKind.InvalidKey)` block, after `Record(...)`:

```csharp
            _log.LogError(e, "sync stopped: Steam rejected the API key");
```

In the final `catch (Exception e)` block, after `Record(...)`:

```csharp
            _log.LogError(e, "sync failed after {Completed} of {Total} games", completed, total);
```

In `Dispose`, replace the body's `Completion.Wait(TimeSpan.FromSeconds(5));` with:

```csharp
        if (!Completion.Wait(TimeSpan.FromSeconds(5)))
        {
            // Shutdown must not hang on a sync that refuses to notice its
            // cancellation — but a timeout here means the orchestrator's
            // workers are still live when the host closes the connections
            // they write through, which is worth knowing about afterwards.
            _log.LogError("shutdown timed out waiting five seconds for the sync to stop");
        }
```

Delete the now-stale `// Bounded rather than indefinite` comment above it, since the replacement comment says the same thing and more.

- [ ] **Step 5: Add the logger factory to `LiveSyncRunner`**

In `src/SteamAchievements.Core/Sync/LiveSyncRunner.cs`, add `using Microsoft.Extensions.Logging;`, add the field, extend the constructor, and log:

```csharp
    private readonly ILoggerFactory _loggers;
    private readonly ILogger<LiveSyncRunner> _log;
```

```csharp
    /// <param name="loggers">
    /// A factory rather than a logger: this class builds a
    /// <see cref="SyncOrchestrator"/> per run, and that orchestrator needs its
    /// own category rather than logging under this one.
    /// </param>
    public LiveSyncRunner(
        ISecretStore secrets,
        GameRepository repository,
        Func<string, SteamApiClient> clientFactory,
        ILoggerFactory loggers)
    {
        _secrets = secrets;
        _repository = repository;
        _clientFactory = clientFactory;
        _loggers = loggers;
        _log = loggers.CreateLogger<LiveSyncRunner>();
    }
```

In `RunAsync`, after `var key = _secrets.Read();`:

```csharp
        // The length, never the value. This is the only place that touches the
        // key at all, and "no key" and "a key of the wrong length" are
        // different first-run failures.
        _log.LogInformation(
            "stored key: {State}",
            string.IsNullOrEmpty(key) ? "none" : $"present, {key.Length} characters");
```

and replace the orchestrator construction with:

```csharp
        var orchestrator = new SyncOrchestrator(
            _clientFactory(key), _repository, SyncOptions.Default,
            _loggers.CreateLogger<SyncOrchestrator>());
```

- [ ] **Step 6: Add the logger to `SyncOrchestrator`**

In `src/SteamAchievements.Core/Sync/SyncOrchestrator.cs`, add `using Microsoft.Extensions.Logging;`, add the field, and extend the constructor — **the logger goes before `retryBaseDelay`, which stays optional and last**:

```csharp
    private readonly ILogger<SyncOrchestrator> _log;
```

```csharp
    public SyncOrchestrator(
        SteamApiClient client,
        GameRepository repository,
        SyncOptions options,
        ILogger<SyncOrchestrator> log,
        TimeSpan? retryBaseDelay = null)
    {
        _client = client;
        _repository = repository;
        _options = options;
        _log = log;
```

In the retry strategy options, add an `OnRetry` callback so a retry is visible:

```csharp
                OnRetry = arguments =>
                {
                    _log.LogWarning(
                        arguments.Outcome.Exception,
                        "retry {Attempt} in {Delay}ms",
                        arguments.AttemptNumber, (long)arguments.RetryDelay.TotalMilliseconds);

                    return default;
                },
```

In the circuit breaker options, add:

```csharp
                OnOpened = arguments =>
                {
                    _log.LogError(
                        "circuit opened for {Duration}ms after repeated transient failures",
                        (long)arguments.BreakDuration.TotalMilliseconds);

                    return default;
                },
                OnClosed = _ =>
                {
                    _log.LogInformation("circuit closed");

                    return default;
                },
```

In `RunAsync`, after `var plan = SyncPlanner.Plan(...)`:

```csharp
        _log.LogInformation(
            "plan: {Planned} of {Owned} owned games need work (force={Force})",
            plan.Count, owned.Count, force);
```

Inside the `Parallel.ForEachAsync` body, after `var done = Interlocked.Increment(ref completed);`:

```csharp
                    _log.LogDebug("progress {Done}/{Total}", done, plan.Count);
```

In `SyncGameAsync`, after `_repository.MarkSynced(item.AppId, item.Playtime, now);`:

```csharp
            _log.LogDebug("game {AppId} synced", item.AppId);
```

In the `catch (SteamApiException e) when (e.Kind == SteamApiErrorKind.NoStatsForApp)` block, before `_repository.MarkNoAchievements(item.AppId);`:

```csharp
            _log.LogDebug("game {AppId} has no achievements", item.AppId);
```

In the `catch (SteamApiException e) when (e.Kind != SteamApiErrorKind.InvalidKey)` block, before `_repository.MarkError(item.AppId, e.Message);`:

```csharp
            _log.LogWarning(e, "game {AppId} failed and was skipped", item.AppId);
```

And in the `if (schema.Count == 0)` branch, before `_repository.MarkNoAchievements(item.AppId);`:

```csharp
                    _log.LogDebug("game {AppId} returned an empty schema", item.AppId);
```

If the `RetryStrategyOptions` or `CircuitBreakerStrategyOptions` callback signatures do not compile as written, consult the Polly v8 API for the exact delegate shape rather than dropping the callback, and record the correction in §10 of the design document.

- [ ] **Step 7: Update every construction site**

`tests/SteamAchievements.Core.Tests/App/SyncCoordinatorTests.cs` — **3** sites. Step 1 fixed the one inside `Build`; the other two construct a coordinator inline (in `DoesNotTreatAPreviousSuccessfulSyncAsAProblem` and in `ReportsARejectedKeyAtStartupWhenTheFlagIsSet`) and each needs `NullLogger<SyncCoordinator>.Instance` as a fifth argument.

`tests/SteamAchievements.Core.Tests/Sync/SyncOrchestratorTests.cs` — **6** sites. Step 1 fixed the one inside `Build`; the remaining five need `NullLogger<SyncOrchestrator>.Instance` as the fourth argument, **before** any `retryBaseDelay`. Watch the sites that pass a retry delay positionally today: they become

```csharp
new SyncOrchestrator(client, repository, SyncOptions.Default,
    NullLogger<SyncOrchestrator>.Instance, TimeSpan.FromMilliseconds(1))
```

Confirm with `git grep -c "new SyncOrchestrator(" -- tests` that you have found all six.

`src/SteamAchievements.Cli/Program.cs` — 1 site, `new SyncOrchestrator(client, repository, SyncOptions.Default)`. Task 10 gives the CLI a real logger factory; until then pass `Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncOrchestrator>.Instance` so the project keeps compiling.

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test tests/SteamAchievements.Core.Tests`
Expected: 366 passed, 0 failed. Every pre-existing test still passes; if one now fails, the cause is a construction site updated wrongly, not the logging.

- [ ] **Step 9: Check the CLI still builds**

Run: `dotnet build src/SteamAchievements.Cli`
Expected: `0 Error(s)`.

- [ ] **Step 10: Format and commit**

```bash
dotnet format src/SteamAchievements.Core
dotnet format src/SteamAchievements.Cli
dotnet format tests/SteamAchievements.Core.Tests
git add src tests
git commit -m "feat: make a sync say what it did, game by game"
```

---

### Task 8: Onboarding, accounts and the reset

**Files:**
- Modify: `src/SteamAchievements.Core/App/OnboardingService.cs`
- Modify: `src/SteamAchievements.Core/App/AccountAdminService.cs`
- Modify: `src/SteamAchievements.Core/Data/Database.cs` (`ResetLibrary`)
- Modify: `src/SteamAchievements.Core/Data/ILibraryReset.cs` (`SqliteLibraryReset`)
- Modify: `tests/SteamAchievements.Core.Tests/App/OnboardingServiceTests.cs` (1 site)
- Modify: `tests/SteamAchievements.Core.Tests/App/AccountAdminServiceTests.cs` (1 site)
- Modify: `tests/SteamAchievements.Core.Tests/Data/DatabaseResetTests.cs` (7 sites)

**Interfaces:**
- Consumes: `RecordingLogger<T>` from Task 6.
- Produces:
  - `OnboardingService(..., ILogger<OnboardingService> log)` — logger last.
  - `AccountAdminService(..., ILogger<AccountAdminService> log)` — logger last.
  - `Database.ResetLibrary(SqliteConnection connection, ILogger log)` — a non-generic `ILogger`, because the caller's category is more useful here than `Database`.
  - `SqliteLibraryReset(SqliteConnection connection, ILogger<SqliteLibraryReset> log)`.

Read each file before editing to get its existing constructor parameter list exactly right; the plan does not repeat parameters it is not changing.

- [ ] **Step 1: Write the failing tests**

Append to `tests/SteamAchievements.Core.Tests/Data/DatabaseResetTests.cs`, which already has a `Populated()` helper returning a `SqliteConnection` with a library in it:

```csharp
    [Fact]
    public void TimesTheVacuumSeparatelyFromTheDeletions()
    {
        // VACUUM against a file with three live connections is the specific
        // untested behaviour on the Windows first-run checklist. "The reset
        // took forty seconds" and "the VACUUM took thirty-nine of them" are
        // different findings, so they are different lines.
        using var connection = Populated();
        var log = new RecordingLogger<SqliteLibraryReset>();

        Database.ResetLibrary(connection, log);

        Assert.True(log.Logged("library emptied"));
        Assert.True(log.Logged("vacuum finished"));
    }
```

In `tests/SteamAchievements.Core.Tests/App/AccountAdminServiceTests.cs`, widen the existing `BuildAsync` helper the way Task 7 widened its neighbours. Add `using Microsoft.Extensions.Logging;` and `using Microsoft.Extensions.Logging.Abstractions;`, then:

```csharp
    private static async Task<(AccountAdminService Admin, MemorySecretStore Secrets, IAccountStore Accounts, Microsoft.Data.Sqlite.SqliteConnection Connection)>
        BuildAsync(string? steamPath = null, ILogger<AccountAdminService>? log = null)
```

and change the construction inside it to:

```csharp
        var admin = new AccountAdminService(
            new SqliteLibraryReset(connection, NullLogger<SqliteLibraryReset>.Instance),
            accounts, secrets,
            new SteamAccountLocator(new FixedSteamPath(steamPath)), community,
            log ?? NullLogger<AccountAdminService>.Instance);
```

Then append:

```csharp
    [Fact]
    public async Task LogsTheResetBecauseItDestroysTheLibrary()
    {
        var log = new RecordingLogger<AccountAdminService>();
        var (admin, _, _, connection) = await BuildAsync(log: log);

        using (connection)
        {
            admin.ResetEverything();
        }

        Assert.True(log.Logged("reset requested"));
        Assert.True(log.Logged("reset finished"));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SteamAchievements.Core.Tests --filter FullyQualifiedName~DatabaseResetTests`
Expected: build failure — `ResetLibrary` takes one argument.

- [ ] **Step 3: Log inside `Database.ResetLibrary`**

In `src/SteamAchievements.Core/Data/Database.cs`, add `using System.Diagnostics;` and `using Microsoft.Extensions.Logging;`, then change the signature and body:

```csharp
    public static void ResetLibrary(SqliteConnection connection, ILogger log)
    {
        var started = Stopwatch.GetTimestamp();

        using (var transaction = connection.BeginTransaction())
        {
            // ... the existing Execute call and Commit, unchanged ...
        }

        log.LogInformation(
            "library emptied in {Elapsed}ms", (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        // VACUUM cannot run inside a transaction, so it is deliberately outside
        // the block above. Without it the file keeps the space the deleted rows
        // occupied, which for a 1500-game library is most of it.
        //
        // Timed on its own because this is the one statement whose behaviour
        // with three live connections against a WAL database has never been
        // observed.
        var vacuumStarted = Stopwatch.GetTimestamp();
        connection.Execute("VACUUM");
        log.LogInformation(
            "vacuum finished in {Elapsed}ms",
            (long)Stopwatch.GetElapsedTime(vacuumStarted).TotalMilliseconds);
    }
```

Keep the existing XML documentation comment above the method as it is.

- [ ] **Step 4: Pass the logger through `SqliteLibraryReset`**

In `src/SteamAchievements.Core/Data/ILibraryReset.cs`, add `using Microsoft.Extensions.Logging;` and change the class:

```csharp
public sealed class SqliteLibraryReset : ILibraryReset
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<SqliteLibraryReset> _log;

    /// <param name="connection">Must be writable and carry a busy timeout — see <see cref="Database.OpenSettings"/>.</param>
    public SqliteLibraryReset(SqliteConnection connection, ILogger<SqliteLibraryReset> log)
    {
        _connection = connection;
        _log = log;
    }

    public void Reset() => Database.ResetLibrary(_connection, _log);
}
```

- [ ] **Step 5: Log in `OnboardingService`**

In `src/SteamAchievements.Core/App/OnboardingService.cs`, add `using Microsoft.Extensions.Logging;`, add an `ILogger<OnboardingService> log` parameter as the last constructor parameter, store it as `_log`, and add these lines:

- wherever the service reports its computed `Step`, log it once per change rather than on every read — put the line in the method that raises `Changed`, immediately before the event is raised:

```csharp
        _log.LogInformation("onboarding step is now {Step}", Step);
```

- in `SubmitKeyAsync`, immediately before returning each outcome, log the outcome and never the key:

```csharp
        _log.LogInformation("key submission outcome: {Outcome}", outcome);
```

  If the method has several `return` statements rather than one `outcome` variable, introduce the variable and return it once, so there is a single place that logs.

- wherever an account is chosen, log the SteamID64:

```csharp
        _log.LogInformation("account chosen steam_id={SteamId}", steamId64);
```

Adapt the parameter names to whatever the file actually calls them.

- [ ] **Step 6: Log in `AccountAdminService`**

In `src/SteamAchievements.Core/App/AccountAdminService.cs`, add `using Microsoft.Extensions.Logging;`, add an `ILogger<AccountAdminService> log` parameter as the last constructor parameter, store it as `_log`, and add:

- at the top of `SwitchToAsync`:

```csharp
        _log.LogWarning(
            "account switch requested from {From} to {To}; the library will be emptied",
            Current?.SteamId64, steamId64);
```

- at the end of `SwitchToAsync`, after the switch has succeeded:

```csharp
        _log.LogInformation("account switch finished steam_id={SteamId}", steamId64);
```

- at the top of `ResetEverything`:

```csharp
        _log.LogWarning("reset requested; the library and the stored key will be deleted");
```

- at the end of `ResetEverything`:

```csharp
        _log.LogInformation("reset finished");
```

- [ ] **Step 7: Update every construction site**

- `tests/SteamAchievements.Core.Tests/Data/DatabaseResetTests.cs` — 7 calls to `Database.ResetLibrary(connection)` become `Database.ResetLibrary(connection, NullLogger.Instance)`, except the new test, which passes its recording logger. Add `using Microsoft.Extensions.Logging.Abstractions;`.
- `tests/SteamAchievements.Core.Tests/App/OnboardingServiceTests.cs` — 1 site, add `NullLogger<OnboardingService>.Instance` last.
- `tests/SteamAchievements.Core.Tests/App/AccountAdminServiceTests.cs` — 1 site, add `NullLogger<AccountAdminService>.Instance` last, except the new test.
- Search for any other construction of these four types: `git grep -n "new SqliteLibraryReset(\|new OnboardingService(\|new AccountAdminService(\|ResetLibrary(" -- src tests`. Every hit must compile.

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test tests/SteamAchievements.Core.Tests`
Expected: 368 passed, 0 failed.

- [ ] **Step 9: Format and commit**

```bash
dotnet format src/SteamAchievements.Core
dotnet format tests/SteamAchievements.Core.Tests
git add src tests
git commit -m "feat: record the two operations that destroy the library"
```

---

### Task 9: `OpenLogFile`, end to end

**Files:**
- Modify: `src/SteamAchievements.Core/Presentation/IExternalLinks.cs`
- Modify: `src/SteamAchievements.Core/App/DataPaths.cs`
- Modify: `src/SteamAchievements.Windows/ShellLinks.cs`
- Modify: `src/SteamAchievements.Preview/Fixtures/FixtureLinks.cs`
- Modify: `src/SteamAchievements.UI/Settings/SettingsPage.razor`
- Modify: `src/SteamAchievements.UI/Settings/SettingsPage.razor.css`
- Test: `tests/SteamAchievements.Core.Tests/App/DataPathsTests.cs` (append; create if absent)

**Interfaces:**
- Consumes: nothing.
- Produces: `IExternalLinks.OpenLogFile()`; `DataPaths` gains `string LogFile` and `public const string LogFileName = "log.txt"`; `ShellLinks(string dataFolder, string logFile)`.

`DataPaths` is where the log path belongs: it is already the single pure function that decides where everything lives, and Task 11 needs the path without recomputing it.

- [ ] **Step 1: Write the failing test**

Append to `tests/SteamAchievements.Core.Tests/App/DataPathsTests.cs` (if the file does not exist, create it following the style of its neighbours in that folder):

```csharp
    [Fact]
    public void PutsTheLogBesideTheDatabase()
    {
        var paths = DataPaths.Resolve(Path.Combine("base", "dir"));

        Assert.Equal(Path.Combine(paths.Folder, "log.txt"), paths.LogFile);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SteamAchievements.Core.Tests --filter FullyQualifiedName~DataPathsTests`
Expected: build failure — `DataPaths` has no `LogFile`.

- [ ] **Step 3: Add the log path to `DataPaths`**

In `src/SteamAchievements.Core/App/DataPaths.cs`:

```csharp
public sealed record DataPaths(string Folder, string DatabaseFile, string SecretFile, string LogFile)
{
    public const string FolderName = "SteamAchievementsTracker";
    public const string DatabaseFileName = "library.db";
    public const string SecretFileName = "apikey.bin";
    public const string LogFileName = "log.txt";

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
            Path.Combine(folder, SecretFileName),
            Path.Combine(folder, LogFileName));
    }
```

Leave `EnsureFolderExists` and the documentation comment unchanged.

- [ ] **Step 4: Extend the seam**

In `src/SteamAchievements.Core/Presentation/IExternalLinks.cs`, add:

```csharp
    /// <summary>The log file, for a user who is reporting a problem.</summary>
    void OpenLogFile();
```

- [ ] **Step 5: Implement it in both hosts**

In `src/SteamAchievements.Windows/ShellLinks.cs`:

```csharp
    private readonly string _dataFolder;
    private readonly string _logFile;

    public ShellLinks(string dataFolder, string logFile)
    {
        _dataFolder = dataFolder;
        _logFile = logFile;
    }

    public void OpenApiKeyPage() => OpenUrl(ApiKeyPage);

    public void OpenDataFolder() => OpenUrl(_dataFolder);

    public void OpenLogFile() => OpenUrl(_logFile);
```

The existing `OpenUrl` and its `catch` stay exactly as they are — a machine with no association for `.txt` throws `Win32Exception`, which it already swallows.

In `src/SteamAchievements.Preview/Fixtures/FixtureLinks.cs`, beside `OpenDataFolder`:

```csharp
    public void OpenLogFile() => OpenUrl("(the log file)");
```

- [ ] **Step 6: Add the row to the settings screen**

In `src/SteamAchievements.UI/Settings/SettingsPage.razor`, add the injection beside the existing ones at the top:

```razor
@inject IExternalLinks Links
```

and inside the `<div class="block">` whose label is `Local data`, immediately after `<div class="label">Local data</div>` and before the reset notice, add:

```razor
        @* Two buttons rather than one. IExternalLinks.OpenDataFolder had no
           caller anywhere in the application — it was written for a button
           nobody built — so adding "Open log" beside a button that does not
           exist would have left that gap in place. *@
        <div class="files">
            <button class="secondary" type="button" @onclick="() => Links.OpenDataFolder()">
                Open data folder
            </button>
            <button class="secondary" type="button" @onclick="() => Links.OpenLogFile()">
                Open log
            </button>
        </div>
        <div class="hint">
            The log records what the application did, with the API key removed. Attach it
            when reporting a problem.
        </div>
```

In `src/SteamAchievements.UI/Settings/SettingsPage.razor.css`, add:

```css
.files {
    display: flex;
    gap: 8px;
    margin-bottom: 12px;
}
```

Read the stylesheet first and match its existing spacing units rather than introducing new ones.

- [ ] **Step 7: Run the tests and build both UI projects**

```bash
dotnet test tests/SteamAchievements.Core.Tests
dotnet build src/SteamAchievements.UI
dotnet build src/SteamAchievements.Preview
```

Expected: 369 passed; both builds `0 Error(s)`.

- [ ] **Step 8: See it in the preview**

```bash
dotnet run --project src/SteamAchievements.Preview
```

Open `http://localhost:5100/settings`. Both buttons must be visible under "Local data". Click each and confirm the strip at the bottom of the page reports `(the data folder)` and `(the log file)`. Stop the host.

If `curl` against localhost returns 503, add `--noproxy '*'`.

- [ ] **Step 9: Format and commit**

```bash
dotnet format src/SteamAchievements.Core
dotnet format src/SteamAchievements.UI
dotnet format src/SteamAchievements.Preview
dotnet format tests/SteamAchievements.Core.Tests
git add src tests
git commit -m "feat: let the settings screen open the log and the data folder"
```

---

### Task 10: The four screens

**Files:**
- Modify: `src/SteamAchievements.UI/Layout/AppShell.razor`
- Modify: `src/SteamAchievements.UI/Queue/QueuePage.razor`
- Modify: `src/SteamAchievements.UI/Sync/SyncPage.razor`
- Modify: `src/SteamAchievements.UI/Settings/SettingsPage.razor`

**Interfaces:**
- Consumes: `ILogger<T>` from the container. The preview registers logging by default; Task 11 registers it in the WPF host.
- Produces: nothing other tasks depend on.

There is no unit test for this task: these are Razor components, and the project has no component test harness. It is verified by building and by the preview click-through in Step 6, and on Windows by the checklist. Do not add a component test framework for it.

- [ ] **Step 1: `AppShell`**

Add at the top, beside the existing injections:

```razor
@inject ILogger<AppShell> Log
```

and `@using Microsoft.Extensions.Logging` if `_Imports.razor` does not already carry it.

Replace `Guard()` with:

```csharp
    private void Guard()
    {
        var step = Onboarding.Step;

        // The destination comes from RouteFor, not from a route constant picked
        // here: that helper exists so the shell and the host cannot disagree
        // about where a given step belongs, and a disagreement shows up as a
        // wrong or blank first screen, discoverable only on Windows. The
        // "not Ready" test stays, though — RouteFor(Ready) is "/", and
        // navigating there unconditionally would throw a user off the settings
        // screen every time this ran.
        if (step != OnboardingStep.Ready)
        {
            var route = OnboardingState.RouteFor(step);

            Log.LogInformation("guard: step={Step}, redirecting to {Route}", step, route);
            Navigation.NavigateTo(route.TrimStart('/'));

            return;
        }

        Log.LogDebug("guard: step={Step}, staying put", step);
    }
```

Replace `HandleLibraryChanged` with:

```csharp
    private void HandleLibraryChanged() => InvokeAsync(() =>
    {
        try
        {
            _summary = Library.GetSummary(Clock.Now);
        }
        // SQLITE_BUSY and SQLITE_LOCKED only — the race against a sync that has
        // just finished writing. Any other code is a real failure and must not
        // be swallowed forever behind stale data.
        catch (SqliteException e) when (e.SqliteErrorCode is 5 or 6)
        {
            Log.LogWarning(e, "summary re-read lost a race with the writer (code {Code})", e.SqliteErrorCode);

            return;
        }
        // Nothing awaits this Task, so without this an escaping exception is
        // simply lost: no message, no crash, a screen that quietly stops
        // updating.
        catch (Exception e)
        {
            Log.LogError(e, "summary re-read failed");

            return;
        }

        StateHasChanged();
    });
```

Wrap the two remaining handlers the same way:

```csharp
    private void HandleStateChanged() => InvokeAsync(() =>
    {
        try
        {
            Guard();
        }
        catch (Exception e)
        {
            Log.LogError(e, "onboarding guard failed");
        }
    });

    private void HandlePreferencesChanged() => InvokeAsync(() =>
    {
        try
        {
            StateHasChanged();
        }
        catch (Exception e)
        {
            Log.LogError(e, "reacting to a preference change failed");
        }
    });
```

- [ ] **Step 2: `QueuePage`**

Add `@inject ILogger<QueuePage> Log`. In the `catch (SqliteException e) when (e.SqliteErrorCode is 5 or 6)` block at line 74, add before `return`:

```csharp
            Log.LogWarning(e, "queue re-read lost a race with the writer (code {Code})", e.SqliteErrorCode);
```

and add a trailing `catch (Exception e)` to the same `try`, logging at `Error` with the message `"queue re-read failed"` and returning, for the reason given in Step 1.

- [ ] **Step 3: `SyncPage`**

Add `@inject ILogger<SyncPage> Log`. In `HandleLibraryChanged`'s filtered catch at line 120, add before `return`:

```csharp
            Log.LogWarning(e, "history re-read lost a race with the writer (code {Code})", e.SqliteErrorCode);
```

Add a trailing `catch (Exception e)` logging `"history re-read failed"` at `Error`. Wrap `HandleStatusChanged`'s body in the same try/catch, logging `"reacting to a sync status change failed"`.

- [ ] **Step 4: `SettingsPage`**

Add `@inject ILogger<SettingsPage> Log`. Three places:

In `HandleLibraryChanged`'s filtered catch (line 258), before `return`:

```csharp
            Log.LogWarning(e, "settings re-read lost a race with the writer (code {Code})", e.SqliteErrorCode);
```

In `Switch`'s unfiltered `catch (SqliteException)` (line 287), change it to capture and log:

```csharp
        catch (SqliteException e)
        {
            Log.LogError(e, "account switch failed to write; the library was not emptied");
            _writeFailed = true;
        }
```

In `Reset`'s unfiltered `catch (SqliteException)` (line 309), the same:

```csharp
        catch (SqliteException e)
        {
            Log.LogError(e, "reset failed to write; the library was not emptied");
            _writeFailed = true;
        }
```

Wrap the bodies of `HandleAccountsChanged`, `HandleSyncChanged` and `HandleLibraryChanged` in try/catch that logs at `Error` and returns, for the reason in Step 1.

- [ ] **Step 5: Build**

```bash
dotnet build src/SteamAchievements.UI
dotnet build src/SteamAchievements.Preview
```

Expected: both `0 Error(s)`.

If `ILogger<T>` cannot be injected because the preview host does not expose it, that is a bug in this step, not in the host: `WebApplication.CreateBuilder` registers `ILogger<T>` by default.

- [ ] **Step 6: Click through the preview and read the console**

```bash
dotnet run --project src/SteamAchievements.Preview
```

Visit each of the eight scenarios and confirm the console shows the guard line for each:

```
http://localhost:5100/?scenario=normal
http://localhost:5100/?scenario=empty
http://localhost:5100/?scenario=invalid-key
http://localhost:5100/?scenario=private-profile
http://localhost:5100/?scenario=rarity-unknown
http://localhost:5100/?scenario=other-account
http://localhost:5100/?scenario=circuit-open
http://localhost:5100/?scenario=first-run
```

`first-run` must log `guard: step=..., redirecting to /onboarding` and land on the onboarding screen. The other seven must log `guard: step=Ready, staying put`. Stop the host.

- [ ] **Step 7: Format and commit**

```bash
dotnet format src/SteamAchievements.UI
git add src/SteamAchievements.UI
git commit -m "fix: stop four screens from swallowing a database failure in silence"
```

---

### Task 11: The preview and the CLI

**Files:**
- Modify: `src/SteamAchievements.Preview/Program.cs`
- Modify: `src/SteamAchievements.Cli/Program.cs`
- Modify: `src/SteamAchievements.Cli/SteamAchievements.Cli.csproj`

**Interfaces:**
- Consumes: `LoggingHandler` (Task 5), `SyncOrchestrator`'s new constructor (Task 7).
- Produces: nothing other tasks depend on.

- [ ] **Step 1: Lower the preview's floor to Debug**

In `src/SteamAchievements.Preview/Program.cs`, immediately after `var builder = WebApplication.CreateBuilder(args);`:

```csharp
// Everything, not the default Information floor. This host exists to exercise
// the screens on macOS, and the lines this work adds are mostly Debug — a
// preview that hides them cannot verify them.
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// ASP.NET Core's own request logging drowns the application's lines and says
// nothing this host needs.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
```

Add `using Microsoft.Extensions.Logging;` at the top.

- [ ] **Step 2: Verify the preview logs**

```bash
dotnet build src/SteamAchievements.Preview
dotnet run --project src/SteamAchievements.Preview
```

Visit `http://localhost:5100/?scenario=first-run` and confirm the console carries `guard:` at `dbug` level and no wall of `Microsoft.AspNetCore` lines. Stop the host.

- [ ] **Step 3: Give the CLI a logger factory**

In `src/SteamAchievements.Cli/SteamAchievements.Cli.csproj`, add to the existing `<ItemGroup>` holding the project reference — as its own `<ItemGroup>` if that one holds only `ProjectReference`:

```xml
  <ItemGroup>
    <!--
      Console logging for a tool whose whole job is to say what the sync
      engine did. Core brings the abstractions transitively; this is the
      console provider and the factory.
    -->
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="10.0.10" />
  </ItemGroup>
```

In `src/SteamAchievements.Cli/Program.cs`, add `using Microsoft.Extensions.Logging;` and `using SteamAchievements.Core.Diagnostics;`, then immediately before `using var connection = Database.Open(dbPath);`:

```csharp
// Debug, because this tool exists for the runs that go wrong.
using var loggers = LoggerFactory.Create(builder => builder
    .SetMinimumLevel(LogLevel.Debug)
    .AddSimpleConsole(console => console.SingleLine = true));
```

Insert `LoggingHandler` into the handler chain. Replace:

```csharp
var innerHandler = new HttpClientHandler();
var countingHandler = new RequestCountingHandler(innerHandler);
HttpMessageHandler outermostHandler = countingHandler;
```

with:

```csharp
var innerHandler = new HttpClientHandler();
var countingHandler = new RequestCountingHandler(innerHandler);

// Outside the counting handler so its own retries are counted once and logged
// once. HttpClient owns and disposes the whole chain.
var loggingHandler = new LoggingHandler(loggers.CreateLogger<LoggingHandler>())
{
    InnerHandler = countingHandler,
};

HttpMessageHandler outermostHandler = loggingHandler;
```

and change the fixture-capturing branch to wrap `loggingHandler` instead of `countingHandler`:

```csharp
    outermostHandler = new FixtureCapturingHandler(loggingHandler, options.DumpFixturesDir, apiKey, steamId.Value.ToString());
```

Replace the `NullLogger<SyncOrchestrator>.Instance` that Task 7 left in place with `loggers.CreateLogger<SyncOrchestrator>()`, and drop the now-unused `using Microsoft.Extensions.Logging.Abstractions;` if it was added.

- [ ] **Step 4: Verify the CLI builds and prints its help**

```bash
dotnet build src/SteamAchievements.Cli
dotnet run --project src/SteamAchievements.Cli
```

Expected: build `0 Error(s)`; running with no arguments prints the "No Steam Web API key found." guidance and exits 1, exactly as before. Do not run it against the real Steam API as part of this task.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test tests/SteamAchievements.Core.Tests`
Expected: 369 passed, 0 failed.

- [ ] **Step 6: Format and commit**

```bash
dotnet format src/SteamAchievements.Preview
dotnet format src/SteamAchievements.Cli
git add src
git commit -m "feat: make both development hosts say what the engine did"
```

---

### Task 12: The WPF host

**Files:**
- Modify: `src/SteamAchievements.Windows/App.xaml.cs`
- Modify: `src/SteamAchievements.Windows/WebView2Probe.cs`
- Modify: `src/SteamAchievements.Windows/SteamAchievements.Windows.csproj`

**Interfaces:**
- Consumes: everything from Tasks 1–11.
- Produces: nothing other tasks depend on.

**This project does not compile on macOS.** Do not run `dotnet build` or `dotnet publish` against it, and do not add `EnableWindowsTargeting`. Verification is: `dotnet format` cannot run either, so the code must be written to match the file's existing style by eye; correctness is established by CI's `build-windows` job and then by the Windows first-run pass. Write carefully — a mistake here costs a full CI cycle.

- [ ] **Step 1: Add the console-free logging packages**

In `src/SteamAchievements.Windows/SteamAchievements.Windows.csproj`, add to the existing `<ItemGroup>` of `PackageReference`s:

```xml
    <!--
      The factory. Microsoft.Extensions.Logging and its Abstractions already
      arrive transitively through Components.WebView.Wpf; naming it here makes
      the dependency the composition root actually uses explicit rather than
      accidental.
    -->
    <PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.10" />
```

- [ ] **Step 2: Report the WebView2 version, not only its presence**

Replace `src/SteamAchievements.Windows/WebView2Probe.cs` with:

```csharp
using Microsoft.Web.WebView2.Core;

namespace SteamAchievements.Windows;

/// <summary>
/// Without the Evergreen runtime a BlazorWebView shows an empty window, which is
/// an unacceptable default answer. Asked before the view is constructed.
/// </summary>
public static class WebView2Probe
{
    /// <summary>
    /// The installed runtime version, or null when there is none. Callers that
    /// only need the yes-or-no answer use <see cref="IsRuntimeInstalled"/>;
    /// the log wants the version, because "an old Evergreen build" and "no
    /// runtime at all" are different problems with different fixes.
    /// </summary>
    public static string? Version()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();

            return string.IsNullOrEmpty(version) ? null : version;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return null;
        }
    }

    public static bool IsRuntimeInstalled() => Version() is not null;
}
```

`IsRuntimeInstalled` keeps its name and meaning, so `MainWindow` and `HostStartupDecision` need no change.

- [ ] **Step 3: Build the logger factory and install the hooks in `App.OnStartup`**

In `src/SteamAchievements.Windows/App.xaml.cs`, add:

```csharp
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamAchievements.Core.Diagnostics;
```

Add the fields beside the existing `_services` and `_connections`:

```csharp
    private ILoggerFactory? _loggers;
    private ILogger<App>? _log;
    private string _logFile = "";
```

Rewrite `OnStartup`:

```csharp
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = DataPaths.Resolve(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        _logFile = paths.LogFile;

        // The order below is deliberate. Resolving the paths and opening the
        // log come first, because they decide *where* anything could be
        // recorded; the only failures they have are ones that leave nowhere to
        // record them, so there is nothing to gain from a second, buffered
        // logger covering these two lines. Everything after this point is
        // logged, including the hooks themselves.
        paths.EnsureFolderExists();

        _loggers = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(new RollingFileLoggerProvider(
                new LogFileOptions(paths.Folder, DataPaths.LogFileName),
                () => DateTimeOffset.UtcNow)));

        _log = _loggers.CreateLogger<App>();

        InstallExceptionHooks();
        LogEnvironment(paths);

        // Built before composition, and therefore still available to the
        // failure screen if composition throws. Opening a URL depends on
        // nothing that can fail here.
        var links = new ShellLinks(paths.Folder, paths.LogFile);

        HostStartup startup;

        try
        {
            startup = Compose(paths, links);
        }
        catch (Exception failure) when (
            failure is SqliteException or IOException or UnauthorizedAccessException)
        {
            _log.LogCritical(failure, "composition failed; showing the failure placard");
            startup = new HostStartup(null, OnboardingState.QueueRoute, failure.Message, paths.Folder, links);
        }

        MainWindow = new MainWindow(startup);
        MainWindow.Show();

        _log.LogInformation("window shown, start path {StartPath}", startup.StartPath);
    }
```

`paths.EnsureFolderExists()` has moved out of the `try` block, because the folder is now needed before composition rather than by it. That is intentional: if the folder cannot be created there is nowhere for the database either, and the exception that used to be caught here would now surface as an unhandled one — which the hooks installed on the next line cannot catch, since they are not installed yet. Wrap it:

```csharp
        try
        {
            paths.EnsureFolderExists();
        }
        catch (Exception folderFailure) when (
            folderFailure is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"The data folder could not be created:\n\n{paths.Folder}\n\n{folderFailure.Message}",
                "Steam Achievements Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);

            return;
        }
```

- [ ] **Step 4: Write `InstallExceptionHooks` and `LogEnvironment`**

Add both methods to `App`:

```csharp
    /// <summary>
    /// The three ways an exception escapes without any of them being visible
    /// today: a crash currently looks like a window that silently disappeared.
    /// </summary>
    private void InstallExceptionHooks()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            _log!.LogCritical(args.Exception, "unhandled exception on the dispatcher thread");

            // Handled, and reported with a MessageBox rather than the startup
            // placard: MainWindow.ShowPlacard is private, and putting the
            // placard back on top of a live BlazorWebView is layout work macOS
            // cannot verify — a bad trade on a path that only runs when
            // something has already gone wrong.
            MessageBox.Show(
                $"Something went wrong:\n\n{args.Exception.Message}\n\nDetails were written to:\n{_logFile}",
                "Steam Achievements Tracker", MessageBoxButton.OK, MessageBoxImage.Error);

            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _log!.LogCritical(
                args.ExceptionObject as Exception,
                "unhandled exception, terminating={Terminating}", args.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log!.LogError(args.Exception, "unobserved task exception");
            args.SetObserved();
        };
    }

    private void LogEnvironment(DataPaths paths)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

        _log!.LogInformation(
            "starting version={Version} os={Os} arch={Arch} process={ProcessArch}",
            version, Environment.OSVersion.VersionString,
            RuntimeInformation.OSArchitecture, RuntimeInformation.ProcessArchitecture);

        _log.LogInformation(
            "webview2 runtime {Version}", WebView2Probe.Version() ?? "not installed");

        _log.LogInformation(
            "folder={Folder} database={Database} exists={Exists} log={Log}",
            paths.Folder, paths.DatabaseFile, File.Exists(paths.DatabaseFile), paths.LogFile);
    }
```

- [ ] **Step 5: Log inside `Compose`**

In `Compose`, time each connection at the call site rather than changing `Database.Open`'s signature — those three methods are called from many tests and the host is the only place that needs the timing:

```csharp
        var openingConnections = Stopwatch.GetTimestamp();

        var writer = Track(Database.Open(paths.DatabaseFile));
        var reader = Track(Database.OpenRead(paths.DatabaseFile));
        var settings = Track(Database.OpenSettings(paths.DatabaseFile));

        _log!.LogInformation(
            "three connections open and the schema migrated in {Elapsed}ms",
            (long)Stopwatch.GetElapsedTime(openingConnections).TotalMilliseconds);
```

Add `using System.Diagnostics;`.

Log the Steam installation path. Replace `var locator = new SteamAccountLocator(new RegistrySteamPathProvider());` with:

```csharp
        // Asked here rather than inside SteamAccountLocator, which would mean
        // giving a Core type a logger for one line. "not found" is the first
        // thing to check when onboarding cannot suggest an account.
        var steamPaths = new RegistrySteamPathProvider();

        _log!.LogInformation("steam installation: {Path}", steamPaths.FindSteamPath() ?? "not found");

        var locator = new SteamAccountLocator(steamPaths);
```

`ISteamPathProvider.FindSteamPath()` returns `string?` and reads the registry each call; calling it twice here is a second registry read and nothing more.

Register the factory so `ILogger<T>` resolves for the components and the Core services, immediately after `var services = new ServiceCollection();`:

```csharp
        // The components and the Core services take ILogger<T>; this is what
        // makes it resolvable. Registered from the existing factory rather than
        // through AddLogging so there is exactly one provider and one file.
        services.AddSingleton(_loggers!);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
```

Change the three registrations whose constructors gained parameters:

```csharp
        services.AddSingleton<ILibraryReset>(
            new SqliteLibraryReset(settings, _loggers!.CreateLogger<SqliteLibraryReset>()));
```

`SyncCoordinator`, `LiveSyncRunner`, `OnboardingService` and `AccountAdminService` are all resolved by the container, so their new `ILogger<T>` and `ILoggerFactory` parameters are satisfied by the two registrations above and need no change here.

Insert `LoggingHandler` into the Steam API chain. Replace:

```csharp
        var steamApi = new HttpClient { BaseAddress = new Uri("https://api.steampowered.com/") };
```

with:

```csharp
        var steamApi = new HttpClient(
            new LoggingHandler(_loggers!.CreateLogger<LoggingHandler>())
            {
                InnerHandler = new HttpClientHandler(),
            })
        {
            BaseAddress = new Uri("https://api.steampowered.com/"),
        };
```

Do the same for the `steamcommunity.com` client used by `SteamCommunityClient`.

After `var step = _services.GetRequiredService<IOnboarding>().Step;`:

```csharp
        _log!.LogInformation("onboarding step at startup: {Step}", step);
```

- [ ] **Step 6: Log the shutdown**

Rewrite `OnExit`:

```csharp
    protected override void OnExit(ExitEventArgs e)
    {
        _log?.LogInformation("shutting down");

        // Order matters as much as it did on the way in: the sync has to be
        // stopped and awaited before the connections it writes through are
        // closed, or the orchestrator's worker pool writes into disposed
        // handles. Disposing the provider is what stops it — the coordinator is
        // a singleton the container owns — so the connection loop comes after.
        _services?.Dispose();
        _log?.LogInformation("services disposed");

        foreach (var connection in _connections)
        {
            connection.Dispose();
        }

        _log?.LogInformation("{Count} connections closed; goodbye", _connections.Count);

        // Last, so everything above reaches the file.
        _loggers?.Dispose();

        base.OnExit(e);
    }
```

- [ ] **Step 7: Re-read the whole file**

Read `src/SteamAchievements.Windows/App.xaml.cs` start to finish. Confirm: every `_log` use is after `_log` is assigned; `using` directives cover `Stopwatch`, `File`, `MessageBox`, `Logger<>`; no `EnableWindowsTargeting` was added anywhere. This is the only review the file gets before CI.

- [ ] **Step 8: Commit**

There is nothing to run locally. Commit and let CI compile it.

```bash
git add src/SteamAchievements.Windows
git commit -m "feat: give the Windows host a log file and three ways to fill it"
```

- [ ] **Step 9: Push and read the CI result**

```bash
git push origin <branch>
gh run watch
```

Expected: `test`, `build-windows` and `build-cli` all green. If `build-windows` fails, fix it and push again — do not proceed to Task 13 with a red build.

---

### Task 13: Documentation

**Files:**
- Create: `docs/windows-first-run.md`
- Modify: `CLAUDE.md`
- Modify: `docs/superpowers/specs/2026-07-27-diagnostics-design.md` (§10)

**Interfaces:**
- Consumes: everything.
- Produces: nothing.

- [ ] **Step 1: Write the checklist**

Create `docs/windows-first-run.md`. It consolidates §9.1 of `docs/superpowers/specs/2026-07-26-windows-host-design.md` — read that section and carry all seven items across verbatim in meaning — and adds the new ones. For every item, say what `log.txt` must contain if it passed.

```markdown
# Windows first run

The application has never run on Windows. This is the single deliberate pass
that establishes whether the host layer works, rather than five fishing trips.

Build: push, then download the `SteamAchievementsTracker-win-x64` artifact from
the run's Actions page. Unpack anywhere and run `SteamAchievements.Windows.exe`.
Data lands in `%LOCALAPPDATA%\SteamAchievementsTracker\`.

Work down the list. For each item, note what happened and what `log.txt` said.

## The artifact

- [ ] `publish/` holds `SteamAchievements.Windows.exe` and a `wwwroot` tree, and
      no loose `.dll` or `.pdb` anywhere under it — the recursive check.

## Starting up

- [ ] The window opens and draws the queue, rather than an empty window.
      **Log:** `starting version=… os=… arch=…`, `webview2 runtime …`,
      `folder=… database=… exists=False`, `three connections open …`,
      `onboarding step at startup: …`, `window shown, start path …`.
- [ ] The RCL's static assets arrived: fonts, `app.css`, the per-component
      isolated CSS, `queue-scroll.js`. A fully working but completely unstyled
      window means the isolated-CSS bundle is named after the wrong assembly.
- [ ] The placeholder gives way to the WebView instead of staying up.

## Onboarding

- [ ] The registry is read and the Steam account is found.
      **Log:** the resolved Steam path, then `account chosen steam_id=…`.
- [ ] The API key page opens in a browser.
- [ ] A key is accepted and stored. **Log:** `key submission outcome: Accepted`
      — and **no key anywhere in the file.** Search it for the key's own text
      before going further; finding it is a leaked credential, not a cosmetic
      bug.
- [ ] DPAPI round-trip: close the application, reopen it, confirm the key is
      still there. **Log:** `stored key: present, 32 characters`.

## The first sync

- [ ] A full sync of the real library completes.
      **Log:** `sync started steam_id=… force=…`, `plan: N of M owned games …`,
      a `game … synced` line per game, `sync completed games=N in …ms`.
- [ ] Pausing mid-sync leaves the counter and the progress bar on screen. The
      detail line reads "idle" — `CurrentGame`, `EtaText` and `RateText` are
      cleared on pause by design.
      **Log:** `sync pause requested`, then `sync paused after N of M games`.
- [ ] Resuming picks up where it stopped rather than starting over.
- [ ] **Closing the application during a running sync.** The provider is
      disposed in `OnExit` while the coordinator is mid-run, and the screens'
      change handlers can fire against a renderer that is already gone.
      **Log:** `shutting down`, `services disposed`, `N connections closed`.
      A `shutdown timed out waiting five seconds` line means the orchestrator's
      workers were still live when the connections closed — a real defect, not
      a slow machine.

## Destructive paths

- [ ] **`VACUUM` inside a reset with three live connections.** Settings →
      Reset database, on a full library. This passes single-connection tests;
      WAL behaves differently with live neighbours, and this has never run.
      **Log:** `reset requested …`, `library emptied in …ms`,
      `vacuum finished in …ms`. If the second number is large or the operation
      fails, that is the finding this item exists for.
- [ ] Switching accounts empties the library and keeps the stored key.

## The log itself

- [ ] `log.txt` exists in `%LOCALAPPDATA%\SteamAchievementsTracker\`.
- [ ] Settings → "Open log" opens it, and "Open data folder" opens the folder.
- [ ] The file is readable while the application is still running.
- [ ] It rotates: after enough syncs, `log.1.txt` appears and the folder never
      exceeds four files.
- [ ] A forced crash — End Task — leaves the last line intact rather than
      truncated.

## Known gaps, so they are not reported as bugs

- `SyncCoordinator` publishes only `SyncProblem.InvalidKey`. The
  "private profile" and "different account" notices on the sync screen are
  reachable only through the preview's fixtures and cannot appear here.
- `SyncPhase.CircuitOpen` is never published either, so the sync screen's
  "waiting to retry" state is likewise unreachable.
```

- [ ] **Step 2: Point the old checklist at the new one**

In `docs/superpowers/specs/2026-07-26-windows-host-design.md`, immediately under the `### 9.1 What only Windows can verify` heading, add:

```markdown
> **Superseded.** The living checklist is `docs/windows-first-run.md`, which
> carries these seven items plus what the log must show for each. This section
> is left as the record of what the host design asked for.
```

- [ ] **Step 3: Update `CLAUDE.md`**

Three edits.

Under "Current state", replace the paragraph beginning "Not wired yet:" — the buttons were wired when that branch merged — with:

```markdown
Every screen is wired to its service: `ISyncController`, `IOnboarding` and
`IAccountAdmin` are reachable from the sync, onboarding and settings screens.

Logging goes through `ILogger<T>`. The file sink lives in `Core/Diagnostics`
and writes `log.txt` beside `library.db`, rotating at 2 MB across four files.
Nothing is filtered: the application has never run on Windows, and the first
failure has to be in the file already. `docs/windows-first-run.md` is the
checklist for that run.
```

In the same section, correct the preview scenario list — there are eight, not five:

```markdown
`?scenario=normal|empty|invalid-key|private-profile|rarity-unknown|other-account|circuit-open|first-run`.
`first-run` is the only way to see `AppShell`'s onboarding guard on macOS.
```

Add three entries to "Facts learned the hard way":

```markdown
- **Redaction belongs in the log writer, not at the call sites.** Steam's
  request URLs carry `key=` in their query string and `SteamApiException`
  messages carry those URLs, so a scrubber you have to remember to call is a
  scrubber that leaks. `Redaction.Scrub` runs inside
  `RollingFileLoggerProvider` on the whole formatted line, exception block
  included, and also masks any bare 32-character uppercase hex token — the
  shape of a Steam key. It deliberately does not match the 40-character
  lowercase SHA-1 hashes in icon URLs.
- **The log writer flushes on every write and never retries after a failure.**
  Buffering loses exactly the lines a crash makes valuable, and a writer that
  retried per line would turn a permissions problem into a nine-minute stall
  during a sync. `RollingFileWriter.Disabled` is set once and never cleared.
- **`SyncOrchestrator` is not resolved from the container.** `LiveSyncRunner`
  builds one per run so a replaced key takes effect, which is why it takes an
  `ILoggerFactory` rather than an `ILogger<T>`.
```

- [ ] **Step 4: Record the divergences**

Fill §10 of `docs/superpowers/specs/2026-07-27-diagnostics-design.md` with what actually differed from the design during Tasks 1–12. Write it from the commits, not from memory. If nothing diverged, say so explicitly rather than leaving the section empty.

- [ ] **Step 5: Commit**

```bash
git add docs CLAUDE.md
git commit -m "docs: write the checklist the first Windows run works from"
```

---

## After Task 13

The branch is complete but unreviewed. A **whole-branch review** is mandatory
before merging: the previous branch's per-task reviews each looked at one task
in isolation and missed defects that were only visible across tasks. Review the
full diff against `main`, not task by task.

Then the Windows pass, against `docs/windows-first-run.md`.
