using Microsoft.Extensions.Logging;
using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests;

/// <summary>
/// The <see cref="ISecretStore"/> every test uses. Shared because the interface
/// is fixed at three methods and there is nothing test-specific left to vary —
/// the two private copies this replaced had already drifted apart, one starting
/// empty and one pre-seeded.
/// </summary>
public sealed class MemorySecretStore : ISecretStore
{
    public MemorySecretStore(string? initial = null) => Secret = initial;

    /// <summary>Exposed so a test can assert on the stored value without going through <see cref="Read"/>.</summary>
    public string? Secret { get; private set; }

    public string? Read() => Secret;

    public void Write(string secret) => Secret = secret;

    public void Clear() => Secret = null;
}

/// <summary>
/// An <see cref="ISteamPathProvider"/> that answers with whatever it was given.
/// <c>new FixedSteamPath(null)</c> is "Steam is not installed".
/// </summary>
public sealed class FixedSteamPath(string? path) : ISteamPathProvider
{
    public string? FindSteamPath() => path;
}

/// <summary>
/// A throwaway Steam installation on disk, holding the committed
/// <c>loginusers.vdf</c> at the path <c>SteamAccountLocator</c> looks for.
///
/// Disposable rather than a bare factory: every call site previously repeated
/// its own <c>try/finally</c> to delete the directory, which is the kind of
/// bookkeeping a using-statement was invented for.
/// </summary>
public sealed class TempSteamRoot : IDisposable
{
    public TempSteamRoot()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "config"));
        File.Copy(TestPaths.Data("loginusers.vdf"), System.IO.Path.Combine(Path, "config", "loginusers.vdf"));
    }

    public string Path { get; }

    /// <summary>The account the committed fixture marks <c>MostRecent</c>.</summary>
    public const ulong ActiveSteamId = 76561190000000002;

    public const string ActiveAccountName = "currentuser";

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

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
