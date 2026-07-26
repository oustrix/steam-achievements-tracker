using SteamAchievements.Core.App;

namespace SteamAchievements.Core.Tests.App;

public class OnboardingStateTests
{
    private static readonly ulong SteamId = 76561190000000002;

    [Fact]
    public void StartsAtAccountSelectionOnAFreshInstall()
    {
        Assert.Equal(OnboardingStep.ChooseAccount, OnboardingState.Evaluate(null, hasKey: false));
    }

    [Fact]
    public void StillAsksForAnAccountWhenAKeyExistsButAnAccountDoesNot()
    {
        Assert.Equal(OnboardingStep.ChooseAccount, OnboardingState.Evaluate(null, hasKey: true));
    }

    [Fact]
    public void TreatsAZeroSteamIdAsNoAccount()
    {
        Assert.Equal(OnboardingStep.ChooseAccount, OnboardingState.Evaluate(0, hasKey: true));
    }

    [Fact]
    public void AsksForAKeyOnceAnAccountIsKnown()
    {
        Assert.Equal(OnboardingStep.EnterKey, OnboardingState.Evaluate(SteamId, hasKey: false));
    }

    [Fact]
    public void IsReadyWithBoth()
    {
        Assert.Equal(OnboardingStep.Ready, OnboardingState.Evaluate(SteamId, hasKey: true));
    }
}
