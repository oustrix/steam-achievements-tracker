using Microsoft.Extensions.Logging;
using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Local;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.App;

public sealed class OnboardingService : IOnboarding
{
    private readonly IAccountStore _accounts;
    private readonly ISecretStore _secrets;
    private readonly SteamAccountLocator _locator;
    private readonly SteamCommunityClient _community;
    private readonly Func<string, SteamApiClient> _clientFactory;
    private readonly ILogger<OnboardingService> _log;

    public OnboardingService(
        IAccountStore accounts,
        ISecretStore secrets,
        SteamAccountLocator locator,
        SteamCommunityClient community,
        Func<string, SteamApiClient> clientFactory,
        ILogger<OnboardingService> log)
    {
        _accounts = accounts;
        _secrets = secrets;
        _locator = locator;
        _community = community;
        _clientFactory = clientFactory;
        _log = log;
    }

    public OnboardingStep Step =>
        OnboardingState.Evaluate(_accounts.Current?.SteamId64, !string.IsNullOrEmpty(_secrets.Read()));

    public IReadOnlyList<SteamAccount> DiscoveredAccounts => _locator.FindAccounts();

    public event Action? Changed;

    public async Task ChooseAccountAsync(ulong steamId64, CancellationToken cancellationToken)
    {
        // The name and the picture are decoration. If the community site is
        // down, redirects, or the profile does not exist, the user still gets
        // through onboarding with a bare SteamID.
        var profile = await _community.GetProfileAsync(steamId64, cancellationToken);

        _accounts.Set(steamId64, profile?.PersonaName ?? string.Empty, profile?.AvatarUrl ?? string.Empty);

        _log.LogInformation("account chosen steam_id={SteamId}", steamId64);

        // Step is computed rather than stored, so the only place that can log a
        // change to it is here, immediately before the event that tells the UI
        // to re-read it — logging inside the Step getter itself would fire on
        // every guard read AppShell makes, not once per change.
        _log.LogInformation("onboarding step is now {Step}", Step);

        Changed?.Invoke();
    }

    /// <summary>
    /// Four exit points, each an early return through <see cref="LogOutcome"/>,
    /// rather than one <c>outcome</c> variable threaded through to a single log
    /// call at the bottom. That single-log-site shape was tried and abandoned:
    /// it forced every branch through one more level of nesting to reach the
    /// line that decided whether to store the key, and it merged two tests
    /// that each checked one branch's behaviour in isolation into one test
    /// that had to know about all of them. Do not reintroduce it — the
    /// duplication of one log call across four call sites is cheaper than
    /// either cost.
    /// </summary>
    public async Task<KeySubmission> SubmitKeyAsync(string pasted, CancellationToken cancellationToken)
    {
        // The one place every branch below passes through on its way out —
        // the key itself never reaches this call, only the outcome does.
        KeySubmission LogOutcome(KeySubmission outcome)
        {
            _log.LogInformation("key submission outcome: {Outcome}", outcome);
            return outcome;
        }

        // Format first, so a typo costs nothing.
        if (!ApiKey.TryNormalize(pasted, out var normalized))
        {
            return LogOutcome(KeySubmission.Malformed);
        }

        var account = _accounts.Current
            ?? throw new InvalidOperationException("An account must be chosen before a key can be checked.");

        try
        {
            // One cheap call that answers "is this key any good". Without it
            // the user finds out several minutes into their first sync
            // instead.
            await _clientFactory(normalized).GetOwnedGamesAsync(account.SteamId64, cancellationToken);
        }
        catch (SteamApiException e) when (e.Kind == SteamApiErrorKind.InvalidKey)
        {
            return LogOutcome(KeySubmission.Rejected);
        }
        catch (SteamApiException)
        {
            // Rate limited, a 5xx, or a transport failure the client already
            // folded into ServerError. The key may well be fine, so it is not
            // called rejected and it is not stored either.
            return LogOutcome(KeySubmission.Unreachable);
        }

        _secrets.Write(normalized);

        // A new key deserves a clean slate: whatever made the previous
        // one look rejected has nothing to do with this one.
        _accounts.ClearKeyRejected();

        var outcome = LogOutcome(KeySubmission.Accepted);

        // Step is computed rather than stored, so the only place that can log a
        // change to it is here, immediately before the event that tells the UI
        // to re-read it — logging inside the Step getter itself would fire on
        // every guard read AppShell makes, not once per change.
        _log.LogInformation("onboarding step is now {Step}", Step);
        Changed?.Invoke();

        return outcome;
    }
}
