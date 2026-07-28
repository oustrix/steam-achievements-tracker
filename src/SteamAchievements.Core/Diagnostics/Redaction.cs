using System.Text.RegularExpressions;

namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// Removes anything secret from a line on its way to the log.
///
/// Applied by the writer rather than at the call sites, because a scrubber you
/// have to remember to call is a scrubber that leaks: Steam's request URLs
/// carry the API key in their query string, and <c>SteamApiException</c>
/// messages carry those URLs, so the secret can reach a log line through paths
/// nobody inspected.
///
/// Both rules are format-based on purpose — the writer is never handed the
/// secret to compare against. Neither can be complete, so the call-site rule
/// stands alongside them: nothing ever passes an API key to a logger.
/// </summary>
public static partial class Redaction
{
    private const string Mask = "***";

    /// <summary>
    /// The parameter name is kept and only the value is masked, so a log line
    /// still shows that a key was sent.
    /// </summary>
    [GeneratedRegex(@"(?<name>\b(?:key|access_token)=)[^&\s]*", RegexOptions.IgnoreCase)]
    private static partial Regex QueryParameter();

    /// <summary>
    /// The shape of a Steam Web API key: exactly 32 uppercase hex characters,
    /// standing alone. Achievement icon URLs carry 40-character lowercase
    /// SHA-1 hashes, which this deliberately does not match.
    /// </summary>
    [GeneratedRegex(@"(?<![0-9A-Za-z])[0-9A-F]{32}(?![0-9A-Za-z])")]
    private static partial Regex ApiKeyShaped();

    public static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var scrubbed = QueryParameter().Replace(text, match => match.Groups["name"].Value + Mask);

        return ApiKeyShaped().Replace(scrubbed, Mask);
    }
}
