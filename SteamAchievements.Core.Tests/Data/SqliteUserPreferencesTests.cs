using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Tests.Data;

public class SqliteUserPreferencesTests
{
    [Fact]
    public void ReportsNoAccentBeforeOneWasEverChosen()
    {
        using var connection = Database.Open(":memory:");
        var preferences = new SqliteUserPreferences(connection);

        Assert.Null(preferences.Accent);
    }

    [Fact]
    public void RoundTripsTheChosenAccent()
    {
        using var connection = Database.Open(":memory:");
        var preferences = new SqliteUserPreferences(connection);

        preferences.SetAccent("#8fb3c9");

        Assert.Equal("#8fb3c9", preferences.Accent);
    }

    [Fact]
    public void OverwritesAPreviouslyChosenAccentInsteadOfAddingARow()
    {
        using var connection = Database.Open(":memory:");
        var preferences = new SqliteUserPreferences(connection);

        preferences.SetAccent("#8fb3c9");
        preferences.SetAccent("#a8b58c");

        Assert.Equal("#a8b58c", preferences.Accent);
        Assert.Equal(1, Dapper.SqlMapper.QuerySingle<long>(connection, "SELECT COUNT(*) FROM settings"));
    }

    [Fact]
    public void PreservesOtherSettingsColumnsWhenWritingTheAccent()
    {
        using var connection = Database.Open(":memory:");
        Dapper.SqlMapper.Execute(connection,
            "INSERT INTO settings (id, persona_name) VALUES (1, 'oustrix')");

        new SqliteUserPreferences(connection).SetAccent("#c98f7a");

        Assert.Equal("oustrix", Dapper.SqlMapper.QuerySingle<string>(
            connection, "SELECT persona_name FROM settings WHERE id = 1"));
    }

    /// <summary>
    /// The accent is read on every shell render and again by the settings
    /// picker, so it is memoized rather than re-queried. Writing the column
    /// behind the instance's back is the only way to observe that from outside,
    /// and it is exactly what nothing in the application does: this type owns
    /// the only write to <c>settings.accent</c>.
    /// </summary>
    [Fact]
    public void ReadsTheAccentFromTheDatabaseOnlyOnce()
    {
        using var connection = Database.Open(":memory:");
        var preferences = new SqliteUserPreferences(connection);

        preferences.SetAccent("#8fb3c9");
        _ = preferences.Accent;

        Dapper.SqlMapper.Execute(connection, "UPDATE settings SET accent = '#a8b58c' WHERE id = 1");

        Assert.Equal("#8fb3c9", preferences.Accent);
    }

    /// <summary>
    /// The cache has to be invalidated by the write that makes it wrong —
    /// including when the first read happened before anything was ever chosen
    /// and cached a null.
    /// </summary>
    [Fact]
    public void ServesTheNewAccentAfterAWriteThatFollowsAnEarlierRead()
    {
        using var connection = Database.Open(":memory:");
        var preferences = new SqliteUserPreferences(connection);

        Assert.Null(preferences.Accent);
        preferences.SetAccent("#c98f7a");

        Assert.Equal("#c98f7a", preferences.Accent);
    }

    /// <summary>
    /// A subscriber (AppShell, in the UI) reads <c>Accent</c> from inside the
    /// handler to repaint. If <c>Changed</c> fired before the write landed,
    /// that read would still see the old colour, so the ordering — not just
    /// "an event fired at all" — is the part worth pinning against a future
    /// refactor of <c>SetAccent</c>.
    /// </summary>
    [Fact]
    public void RaisesChangedOnlyAfterTheNewAccentIsAlreadyPersisted()
    {
        using var connection = Database.Open(":memory:");
        var preferences = new SqliteUserPreferences(connection);
        string? accentSeenByHandler = null;

        preferences.Changed += () => accentSeenByHandler = preferences.Accent;
        preferences.SetAccent("#a8b58c");

        Assert.Equal("#a8b58c", accentSeenByHandler);
    }
}
