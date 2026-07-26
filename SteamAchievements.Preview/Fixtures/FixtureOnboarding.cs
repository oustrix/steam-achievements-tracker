using SteamAchievements.Core.App;
using SteamAchievements.Core.Local;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>
/// Onboarding without Steam and without a network.
///
/// The four outcomes of a key submission are reachable by what is typed, and
/// the rule is printed under the field rather than left to be found in this
/// file: 32 hexadecimal characters is accepted, "reject" is refused, "offline"
/// is unreachable, anything else is malformed.
/// </summary>
public sealed class FixtureOnboarding : IOnboarding
{
    public const string RejectTrigger = "reject";
    public const string UnreachableTrigger = "offline";

    public const ulong FixtureSteamId = 76561190000000001;

    private readonly FixtureLibraryQuery _library;

    private ulong? _chosen;
    private bool? _keyStored;

    public FixtureOnboarding(FixtureLibraryQuery library) => _library = library;

    /// <summary>
    /// Every scenario except first-run represents a machine already past
    /// onboarding — without that, AppShell's guard would send every screen in
    /// the preview to /onboarding.
    ///
    /// Read on each access rather than captured in the constructor: the scenario
    /// is set by ScenarioScope while the page renders, which is after the
    /// container has built this. A constructor reading it sees Normal every
    /// time, and ?scenario=first-run would silently do nothing.
    /// </summary>
    public OnboardingStep Step => OnboardingState.Evaluate(
        _chosen ?? (_library.Scenario == Scenario.FirstRun ? null : FixtureSteamId),
        _keyStored ?? _library.Scenario != Scenario.FirstRun);

    public IReadOnlyList<SteamAccount> DiscoveredAccounts =>
    [
        new SteamAccount(76561190000000001, "someone", "Someone", MostRecent: true, FixtureData.Now),
        new SteamAccount(76561190000000002, "otherperson", "Other Person", MostRecent: false, FixtureData.Now.AddDays(-30)),
    ];

    public event Action? Changed;

    public Task ChooseAccountAsync(ulong steamId64, CancellationToken cancellationToken)
    {
        _chosen = steamId64;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public async Task<KeySubmission> SubmitKeyAsync(string pasted, CancellationToken cancellationToken)
    {
        // Long enough to see the controls disabled, short enough not to be a
        // wait. The preview is read as much as it is clicked.
        await Task.Delay(400, cancellationToken);

        var trimmed = pasted.Trim();

        if (trimmed.Equals(RejectTrigger, StringComparison.OrdinalIgnoreCase))
        {
            return KeySubmission.Rejected;
        }

        if (trimmed.Equals(UnreachableTrigger, StringComparison.OrdinalIgnoreCase))
        {
            return KeySubmission.Unreachable;
        }

        if (!ApiKey.TryNormalize(trimmed, out _))
        {
            return KeySubmission.Malformed;
        }

        _keyStored = true;
        Changed?.Invoke();
        return KeySubmission.Accepted;
    }
}
