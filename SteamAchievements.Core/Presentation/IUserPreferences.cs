namespace SteamAchievements.Core.Presentation;

/// <summary>
/// The only thing the UI writes. Kept separate from <c>ILibraryQuery</c> so
/// that interface stays honestly read-only and the single write is visible in
/// the type system rather than buried in a general-purpose repository.
/// </summary>
public interface IUserPreferences
{
    /// <summary>The chosen accent colour, or null while the default applies.</summary>
    string? Accent { get; }

    void SetAccent(string accent);
}
