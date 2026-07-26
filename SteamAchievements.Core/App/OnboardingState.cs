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
    public const string OnboardingRoute = "/onboarding";
    public const string QueueRoute = "/";

    public static OnboardingStep Evaluate(ulong? storedSteamId, bool hasKey)
    {
        if (storedSteamId is null or 0)
        {
            return OnboardingStep.ChooseAccount;
        }

        return hasKey ? OnboardingStep.Ready : OnboardingStep.EnterKey;
    }

    /// <summary>
    /// Where a given step belongs. Two callers need this answer — the host,
    /// picking the WebView's start path, and <c>AppShell</c>, deciding whether
    /// to redirect instead of drawing its chrome — and they must not disagree.
    /// A disagreement shows up as a wrong or blank first screen, discoverable
    /// only on Windows.
    /// </summary>
    public static string RouteFor(OnboardingStep step) =>
        step == OnboardingStep.Ready ? QueueRoute : OnboardingRoute;
}
