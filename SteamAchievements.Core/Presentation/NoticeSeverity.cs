namespace SteamAchievements.Core.Presentation;

/// <summary>
/// How loud a notice is.
///
/// In Core rather than in the UI project because Core decides it:
/// <see cref="KeySubmissionMessage"/> returns the severity of the message it
/// describes, and Core cannot reference SteamAchievements.UI. A parallel enum
/// on this side plus a mapping in the component would put the same four-way
/// table in two places, which is exactly what that type exists to prevent.
/// </summary>
public enum NoticeSeverity
{
    Info,
    Warning,
    Danger,
}
