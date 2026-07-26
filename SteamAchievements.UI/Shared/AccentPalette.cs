namespace SteamAchievements.UI.Shared;

/// <summary>
/// The accents the mockup offers, and the one shown before anyone has chosen.
///
/// One home for both, because they are the same decision seen twice: the
/// default is the first swatch. Held apart, the shell could fall back to a
/// colour that no swatch in the picker matches, and nothing would mark that
/// as selected. Also keeps a page from importing the shell layout's type
/// merely to read a colour.
///
/// These values are data — the accent travels through IUserPreferences as a
/// string and is written onto the shell as an inline custom property — which
/// is why they live here rather than in app.css with the rest of the palette.
/// </summary>
public static class AccentPalette
{
    public static readonly (string Value, string Name)[] All =
    [
        ("#e0a355", "Amber"),
        ("#8fb3c9", "Blue"),
        ("#c98f7a", "Terracotta"),
        ("#a8b58c", "Olive"),
    ];

    public static string Default => All[0].Value;
}
