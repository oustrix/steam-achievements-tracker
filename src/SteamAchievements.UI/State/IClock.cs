namespace SteamAchievements.UI.State;

/// <summary>
/// The screens need the current time to pass into ILibraryQuery, and
/// Core/Presentation deliberately refuses to read it. This is where that
/// boundary is crossed, in one place, so the preview host can freeze it.
/// </summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
