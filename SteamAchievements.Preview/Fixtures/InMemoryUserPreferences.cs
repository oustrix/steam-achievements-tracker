using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>Preview-only: the accent picker works, it just does not survive a restart.</summary>
public sealed class InMemoryUserPreferences : IUserPreferences
{
    public string? Accent { get; private set; }

    public void SetAccent(string accent) => Accent = accent;
}
