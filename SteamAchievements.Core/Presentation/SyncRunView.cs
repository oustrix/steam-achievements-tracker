namespace SteamAchievements.Core.Presentation;

public sealed record SyncRunView(
    string WhenText,
    string WhatText,
    string DurationText,
    bool Failed);
