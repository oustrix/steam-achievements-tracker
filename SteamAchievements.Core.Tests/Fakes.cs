using SteamAchievements.Core.Abstractions;

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
