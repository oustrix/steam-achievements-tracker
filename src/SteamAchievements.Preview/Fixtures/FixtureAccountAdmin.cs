using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>
/// Switching and resetting really empty the fixture library, so the two-step
/// confirmations are verified by their effect rather than by their appearance.
///
/// The stored account and the stored key live in <see cref="FixtureState"/>,
/// which FixtureOnboarding reads: a reset here has to change what
/// <c>IOnboarding.Step</c> answers, exactly as it does in production, or
/// AppShell's guard never fires and the preview cannot show it.
/// </summary>
public sealed class FixtureAccountAdmin(FixtureLibraryQuery library, FixtureState state) : IAccountAdmin
{
    public StoredAccount? Current => state.Account;

    public AccountMismatch? Mismatch => library.Scenario == Scenario.OtherAccount && Current is not null
        ? new AccountMismatch(76561190000000002, "otherperson")
        : null;

    public event Action? Changed;

    public Task SwitchToAsync(ulong steamId64, CancellationToken cancellationToken)
    {
        library.Cleared = true;

        // The key survives a switch — a Steam key is not bound to an account —
        // so this stores an account rather than clearing everything.
        state.Store(new StoredAccount(steamId64, "otherperson", ""));

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public void ResetEverything()
    {
        library.Cleared = true;
        state.Clear();

        Changed?.Invoke();
    }
}
