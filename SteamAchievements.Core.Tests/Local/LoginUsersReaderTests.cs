using SteamAchievements.Core.Local;

namespace SteamAchievements.Core.Tests.Local;

public class LoginUsersReaderTests
{
    private static string Fixture() => File.ReadAllText(TestPaths.Data("loginusers.vdf"));

    [Fact]
    public void ReadsAllAccounts()
    {
        var accounts = LoginUsersReader.Read(Fixture());

        Assert.Equal(2, accounts.Count);
        Assert.Contains(accounts, a => a.SteamId64 == 76561190000000002 && a.AccountName == "currentuser");
    }

    [Fact]
    public void SelectsAccountFlaggedMostRecent()
    {
        var active = LoginUsersReader.SelectActive(LoginUsersReader.Read(Fixture()));

        Assert.NotNull(active);
        Assert.Equal(76561190000000002u, active!.SteamId64);
    }

    [Fact]
    public void FallsBackToNewestTimestampWhenNoMostRecentFlag()
    {
        var accounts = LoginUsersReader.Read("""
            "users"
            {
                "76561190000000001"
                {
                    "AccountName"		"a"
                    "PersonaName"		"A"
                    "MostRecent"		"0"
                    "Timestamp"		"1000"
                }
                "76561190000000002"
                {
                    "AccountName"		"b"
                    "PersonaName"		"B"
                    "MostRecent"		"0"
                    "Timestamp"		"2000"
                }
            }
            """);

        Assert.Equal(76561190000000002u, LoginUsersReader.SelectActive(accounts)!.SteamId64);
    }

    [Fact]
    public void ReturnsNullWhenFileHasNoUsers()
    {
        Assert.Null(LoginUsersReader.SelectActive(LoginUsersReader.Read("\"users\"\n{\n}\n")));
    }

    [Fact]
    public void SkipsEntriesWithUnparseableSteamId()
    {
        var accounts = LoginUsersReader.Read("""
            "users"
            {
                "not-a-number"
                {
                    "AccountName"		"broken"
                }
            }
            """);

        Assert.Empty(accounts);
    }
}
