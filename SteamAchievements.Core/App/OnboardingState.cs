namespace SteamAchievements.Core.App;

public enum OnboardingStep
{
    ChooseAccount,
    EnterKey,
    Ready,
}

/// <summary>
/// The whole of "is onboarding complete", as two inputs and one output. The
/// shell reads this to decide whether to draw its chrome, and the host reads it
/// to decide the WebView's start path.
/// </summary>
public static class OnboardingState
{
    public static OnboardingStep Evaluate(ulong? storedSteamId, bool hasKey)
    {
        if (storedSteamId is null or 0)
        {
            return OnboardingStep.ChooseAccount;
        }

        return hasKey ? OnboardingStep.Ready : OnboardingStep.EnterKey;
    }
}
