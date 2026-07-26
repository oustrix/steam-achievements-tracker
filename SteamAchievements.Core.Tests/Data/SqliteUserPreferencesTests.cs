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
}
