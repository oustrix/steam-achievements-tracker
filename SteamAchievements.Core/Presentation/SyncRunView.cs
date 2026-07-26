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
    SyncRunOutcome Outcome);
