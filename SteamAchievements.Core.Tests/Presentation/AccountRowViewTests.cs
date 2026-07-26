using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Core.Tests.Presentation;

public class AccountRowViewTests
{
    private static readonly StoredAccount Stored =
        new(76561190000000001, "someone", "https://avatars.example/1.jpg");

    [Fact]
    public void SaysNobodyIsSignedInBeforeOnboarding()
    {
        var row = AccountRowView.For(stored: null, mismatch: null);

        Assert.Equal(AccountRowView.NoAccount, row.Name);
        Assert.Null(row.AvatarUrl);
        Assert.Null(row.SwitchPrompt);
        Assert.Null(row.SwitchTarget);
    }

    [Fact]
    public void ShowsTheStoredAccount()
    {
        var row = AccountRowView.For(Stored, mismatch: null);

        Assert.Equal("someone", row.Name);
        Assert.Contains("76561190000000001", row.Detail);
        Assert.Equal("https://avatars.example/1.jpg", row.AvatarUrl);
    }

    /// <summary>
    /// A real path, not a defensive branch: ChooseAccountAsync stores an empty
    /// persona name whenever steamcommunity does not answer or the profile is
    /// private, and the row must still name somebody.
    /// </summary>
    [Fact]
    public void FallsBackToTheSteamIdWhenTheProfileNeverAnswered()
    {
        var row = AccountRowView.For(Stored with { PersonaName = "", AvatarUrl = "" }, mismatch: null);

        Assert.Equal("76561190000000001", row.Name);
        Assert.Null(row.AvatarUrl);
    }

    [Fact]
    public void OffersTheActiveAccountWhenSteamDisagrees()
    {
        var row = AccountRowView.For(Stored, new AccountMismatch(76561190000000002, "currentuser"));

        Assert.Equal("someone", row.Name);
        Assert.Contains("currentuser", row.SwitchPrompt);
        Assert.Equal(76561190000000002UL, row.SwitchTarget);
    }

    [Fact]
    public void OffersTheActiveAccountEvenWithNothingStored()
    {
        var row = AccountRowView.For(stored: null, new AccountMismatch(76561190000000002, "currentuser"));

        Assert.Equal(AccountRowView.NoAccount, row.Name);
        Assert.Equal(76561190000000002UL, row.SwitchTarget);
    }
}
