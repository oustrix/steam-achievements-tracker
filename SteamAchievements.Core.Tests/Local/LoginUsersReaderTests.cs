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

    [Fact]
    public void ClampsOutOfRangeTimestampToMinValue()
    {
        var accounts = LoginUsersReader.Read("""
            "users"
            {
                "76561190000000001"
                {
                    "AccountName"		"bad_timestamp"
                    "PersonaName"		"Bad"
                    "MostRecent"		"0"
                    "Timestamp"		"99999999999999"
                }
                "76561190000000002"
                {
                    "AccountName"		"good"
                    "PersonaName"		"Good"
                    "MostRecent"		"0"
                    "Timestamp"		"1000"
                }
            }
            """);

        Assert.Equal(2, accounts.Count);
        var badEntry = accounts.First(a => a.SteamId64 == 76561190000000001);
        Assert.Equal(DateTimeOffset.MinValue, badEntry.Timestamp);
    }

    [Fact]
    public void BreaksTieByTimestampWhenMultipleAccountsFlaggedMostRecent()
    {
        var accounts = LoginUsersReader.Read("""
            "users"
            {
                "76561190000000001"
                {
                    "AccountName"		"older"
                    "PersonaName"		"Older"
                    "MostRecent"		"1"
                    "Timestamp"		"1000"
                }
                "76561190000000002"
                {
                    "AccountName"		"newer"
                    "PersonaName"		"Newer"
                    "MostRecent"		"1"
                    "Timestamp"		"2000"
                }
            }
            """);

        Assert.Equal(76561190000000002u, LoginUsersReader.SelectActive(accounts)!.SteamId64);
    }

    [Fact]
    public void SelectsFlaggedAccountWithCorruptedTimestampOverValidOlderAccount()
    {
        var accounts = LoginUsersReader.Read("""
            "users"
            {
                "76561190000000001"
                {
                    "AccountName"		"flagged"
                    "PersonaName"		"Flagged"
                    "MostRecent"		"1"
                    "Timestamp"		"99999999999999"
                }
                "76561190000000002"
                {
                    "AccountName"		"valid"
                    "PersonaName"		"Valid"
                    "MostRecent"		"0"
                    "Timestamp"		"1000"
                }
            }
            """);

        var active = LoginUsersReader.SelectActive(accounts);
        Assert.NotNull(active);
        Assert.Equal(76561190000000001u, active!.SteamId64);
    }
}
