namespace SteamAchievements.Core.Presentation;

public sealed record SyncRunView(
    string WhenText,
    string WhatText,
    string DurationText,
    bool Failed)
{
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
        if (error is not null)
        {
            return $"Failed — {error}";
        }

        var count = Formatting.Number(gamesSynced);

        return kind switch
        {
            "full" => $"Full sync — {count} games",
            "incremental" => $"Incremental — {count} games changed",
            "schema" => $"Schema refresh — {count} games stale",
            _ => $"{kind} — {count} games",
        };
    }
}
