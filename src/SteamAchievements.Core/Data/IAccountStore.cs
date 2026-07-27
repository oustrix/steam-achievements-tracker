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
