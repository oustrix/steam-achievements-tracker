namespace SteamAchievements.Core.Presentation;

public sealed record KeyMessage(string Title, string Body, NoticeSeverity Severity);

/// <summary>
/// What the user is told about a submitted key.
///
/// Here rather than in the component because the distinction this table
/// carries is the reason <see cref="IOnboarding.SubmitKeyAsync"/> returns four
/// values instead of a boolean: a key Steam refused needs a different key, and
/// a Steam that could not be reached needs another attempt. Advice that swaps
/// those two sends the user to Steam's website for a key that was already fine.
/// </summary>
public static class KeySubmissionMessage
{
    public static KeyMessage For(KeySubmission result) => result switch
    {
        KeySubmission.Malformed => new KeyMessage(
            "That does not look like a key",
            "A Steam Web API key is 32 hexadecimal characters. Nothing was sent to Steam.",
            NoticeSeverity.Warning),

        KeySubmission.Rejected => new KeyMessage(
            "Steam refused this key",
            "The key was revoked or mistyped. Retrying will not help — issue another key on Steam's key page.",
            NoticeSeverity.Danger),

        KeySubmission.Unreachable => new KeyMessage(
            "Steam could not be reached",
            "The key was not checked and nothing was stored. It may well be fine — try again.",
            NoticeSeverity.Warning),

        _ => new KeyMessage(
            "Key stored",
            "The key is stored, encrypted for this Windows account.",
            NoticeSeverity.Info),
    };
}
