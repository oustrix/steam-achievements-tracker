namespace SteamAchievements.Core.Presentation;

/// <summary>
/// How a recorded sync ended. A cancelled run is not a failure: pausing is
/// implemented as cancel-and-resume, so treating any non-null error as a
/// failure would report every pause as one.
/// </summary>
public enum SyncRunOutcome
{
    Completed,
    Cancelled,
    Failed,
}

public sealed record SyncRunView(
    string WhenText,
    string WhatText,
    string DurationText,
    SyncRunOutcome Outcome)
{
    /// <summary>
    /// What a user-stopped run stores in <c>sync_runs.error</c>. The column has
    /// no separate outcome field, so this sentinel is the contract between the
    /// writer and the screen — declared once, here, where both can see it.
    /// </summary>
    public const string CancelledMarker = "cancelled";

    public static SyncRunOutcome OutcomeOf(string? error) => error switch
    {
        null => SyncRunOutcome.Completed,
        CancelledMarker => SyncRunOutcome.Cancelled,
        _ => SyncRunOutcome.Failed,
    };

    /// <summary>
    /// The sentence describing one sync run.
    ///
    /// Lives here rather than beside the SQL because the preview host renders
    /// the same history from fixtures. When each wrote its own copy, a change
    /// to this wording reached the real screen and left the preview showing the
    /// old text — which defeats the point of having a preview.
    /// </summary>
    public static string Describe(string kind, long gamesSynced, string? error)
    {
        var count = Formatting.Number(gamesSynced);

        return OutcomeOf(error) switch
        {
            SyncRunOutcome.Cancelled => $"Cancelled — {count} games",
            SyncRunOutcome.Failed => $"Failed — {error}",
            _ => kind switch
            {
                "full" => $"Full sync — {count} games",
                "incremental" => $"Incremental — {count} games changed",
                "schema" => $"Schema refresh — {count} games stale",
                _ => $"{kind} — {count} games",
            },
        };
    }
}
