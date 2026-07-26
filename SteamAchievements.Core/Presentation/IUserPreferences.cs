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

    /// <summary>
    /// Raised after the accent changes. The shell renders the accent as a CSS
    /// variable on its root, and a Blazor layout does not re-render when a page
    /// inside it changes state — so without this the new colour would not
    /// appear until the next navigation.
    /// </summary>
    event Action? Changed;
}
