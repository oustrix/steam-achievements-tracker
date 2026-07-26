namespace SteamAchievements.Core.App;

/// <summary>
/// A Steam Web API key is 32 hexadecimal characters. Checking that before
/// spending a request is worth the twenty lines, and doing it here rather than
/// in the screen keeps it under test.
/// </summary>
public static class ApiKey
{
    public const int Length = 32;

    /// <summary>
    /// Accepts only text that is a key <em>in its entirety</em> once the
    /// wrapping a paste tends to add — spaces, newlines, quotes — is removed.
    /// It deliberately does not search for a 32-character run inside a longer
    /// document: that would match MD5 sums and git hashes.
    /// </summary>
    public static bool TryNormalize(string? candidate, out string normalized)
    {
        normalized = string.Empty;

        if (candidate is null)
        {
            return false;
        }

        var trimmed = candidate.Trim().Trim('"', '\'').Trim();

        if (trimmed.Length != Length)
        {
            return false;
        }

        foreach (var character in trimmed)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        // Steam displays keys in upper case; normalizing means a key pasted
        // twice in different cases is recognised as the same key.
        normalized = trimmed.ToUpperInvariant();
        return true;
    }
}
