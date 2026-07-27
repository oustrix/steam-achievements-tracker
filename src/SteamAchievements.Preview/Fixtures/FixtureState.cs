using SteamAchievements.Core.Data;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>
/// The one copy of "an account is stored" and "a key is stored", shared by
/// <see cref="FixtureOnboarding"/> and <see cref="FixtureAccountAdmin"/>.
///
/// Production has no choice about this: <c>OnboardingService</c> reads
/// <c>IAccountStore</c> and <c>ISecretStore</c>, and <c>AccountAdminService</c>
/// writes the same two, so <c>ResetEverything</c> clears exactly what
/// <c>IOnboarding.Step</c> reads and <c>AppShell</c>'s guard carries the user
/// back to onboarding. Two fixtures with private copies disagreed: after a
/// reset the settings screen still said the key was stored and nothing
/// redirected — which left the guard of design section 3.3 unverifiable on
/// macOS, in the single case it was written for.
///
/// The scenario is read on every access rather than captured: ScenarioScope
/// sets it while the page renders, which is after the container has built this.
/// A constructor reading it would see <c>Normal</c> every time, and
/// <c>?scenario=first-run</c> would silently do nothing.
/// </summary>
public sealed class FixtureState(FixtureLibraryQuery library)
{
    public const ulong FixtureSteamId = 76561190000000001;

    /// <summary>Who every scenario but first-run has already onboarded as.</summary>
    private static readonly StoredAccount Onboarded = new(FixtureSteamId, "someone", "");

    private StoredAccount? _stored;
    private bool? _keyStored;
    private bool _wiped;

    public StoredAccount? Account => _wiped ? null : _stored ?? (FirstRun ? null : Onboarded);

    public bool KeyStored => _keyStored ?? !FirstRun;

    private bool FirstRun => library.Scenario == Scenario.FirstRun;

    public void Store(StoredAccount account)
    {
        _wiped = false;
        _stored = account;
    }

    public void StoreKey() => _keyStored = true;

    /// <summary>
    /// What <c>ResetEverything</c> clears — the account and the key both, which
    /// is what makes the onboarding step fall back to ChooseAccount.
    /// A switch does not come through here: it keeps the key.
    /// </summary>
    public void Clear()
    {
        _wiped = true;
        _stored = null;
        _keyStored = false;
    }
}
