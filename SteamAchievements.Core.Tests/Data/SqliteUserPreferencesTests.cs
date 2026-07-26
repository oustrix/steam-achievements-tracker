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
