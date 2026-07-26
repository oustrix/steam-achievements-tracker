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
