using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Preview.Fixtures;

/// <summary>
/// Switching and resetting really empty the fixture library, so the two-step
/// confirmations are verified by their effect rather than by their appearance.
/// </summary>
public sealed class FixtureAccountAdmin : IAccountAdmin
{
    private readonly FixtureLibraryQuery _library;

    private StoredAccount? _switched;
    private bool _reset;

    public FixtureAccountAdmin(FixtureLibraryQuery library) => _library = library;

    /// <summary>
    /// Derived on each access, not captured in the constructor: ScenarioScope
    /// sets the scenario while the page renders, after the container has built
    /// this. See FixtureOnboarding.Step for the same reason at more length.
    /// </summary>
    public StoredAccount? Current => _reset
        ? null
        : _switched ?? (_library.Scenario == Scenario.FirstRun
            ? null
            : new StoredAccount(FixtureOnboarding.FixtureSteamId, "someone", ""));

    public AccountMismatch? Mismatch => _library.Scenario == Scenario.OtherAccount && Current is not null
        ? new AccountMismatch(76561190000000002, "otherperson")
        : null;

    public event Action? Changed;

    public Task SwitchToAsync(ulong steamId64, CancellationToken cancellationToken)
    {
        _library.Cleared = true;
        _reset = false;
        _switched = new StoredAccount(steamId64, "otherperson", "");

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public void ResetEverything()
    {
        _library.Cleared = true;
        _switched = null;
        _reset = true;

        Changed?.Invoke();
    }
}
