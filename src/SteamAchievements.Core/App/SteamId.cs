namespace SteamAchievements.Core.App;

/// <summary>
/// Manual entry for when Steam is not installed, or the user is not signed into
/// it on this machine.
/// </summary>
public static class SteamId
{
    private const string ProfilesSegment = "/profiles/";

    /// <summary>
    /// Accepts a 17-digit SteamID64 or a <c>/profiles/&lt;id&gt;</c> URL.
    ///
    /// Vanity URLs (<c>/id/&lt;name&gt;</c>) are deliberately rejected: resolving
    /// one needs an endpoint that is not in docs/steam-api.md, and adding an
    /// endpoint means verifying it against live requests first.
    /// </summary>
    public static bool TryParse(string? candidate, out ulong steamId)
    {
        steamId = 0;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var text = candidate.Trim();
        var segment = text.IndexOf(ProfilesSegment, StringComparison.OrdinalIgnoreCase);

        if (segment >= 0)
        {
            text = text[(segment + ProfilesSegment.Length)..].Trim('/');
        }

        // A SteamID64 is always 17 digits. Length is checked as well as
        // parseability so a truncated paste is rejected rather than accepted as
        // a small number.
        if (text.Length != 17 || !ulong.TryParse(text, out var parsed))
        {
            return false;
        }

        steamId = parsed;
        return true;
    }
}
