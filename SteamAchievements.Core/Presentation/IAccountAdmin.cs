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
