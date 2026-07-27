using SteamAchievements.Core.App;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Local;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>
/// Onboarding without Steam and without a network.
///
/// The four outcomes of a key submission are reachable by what is typed, and
/// the rule is printed under the field by the preview host rather than left to
/// be found in this file: 32 hexadecimal characters is accepted,
/// <see cref="RejectTrigger"/> is refused, <see cref="UnreachableTrigger"/> is
/// unreachable, anything else is malformed.
///
/// What is stored lives in <see cref="FixtureState"/>, not here, because
/// FixtureAccountAdmin writes the same two facts and production has them
/// reading one store each.
/// </summary>
public sealed class FixtureOnboarding(FixtureState state) : IOnboarding
{
    public const string RejectTrigger = "reject";
    public const string UnreachableTrigger = "offline";

    public OnboardingStep Step => OnboardingState.Evaluate(state.Account?.SteamId64, state.KeyStored);

    public IReadOnlyList<SteamAccount> DiscoveredAccounts =>
    [
        new SteamAccount(FixtureState.FixtureSteamId, "someone", "Someone", MostRecent: true, FixtureData.Now),
        new SteamAccount(76561190000000002, "otherperson", "Other Person", MostRecent: false, FixtureData.Now.AddDays(-30)),
    ];

    public event Action? Changed;

    public Task ChooseAccountAsync(ulong steamId64, CancellationToken cancellationToken)
    {
        // An account typed by hand has no persona name here, which is the same
        // state ChooseAccountAsync reaches when steamcommunity does not answer —
        // and the case the settings row's SteamID64 fallback is written for.
        var discovered = DiscoveredAccounts.FirstOrDefault(a => a.SteamId64 == steamId64);

        state.Store(new StoredAccount(steamId64, discovered?.PersonaName ?? "", ""));
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public async Task<KeySubmission> SubmitKeyAsync(string pasted, CancellationToken cancellationToken)
    {
        if (state.Account is null)
        {
            throw new InvalidOperationException("An account must be chosen before a key can be checked.");
        }

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

        state.StoreKey();
        Changed?.Invoke();
        return KeySubmission.Accepted;
    }
}
