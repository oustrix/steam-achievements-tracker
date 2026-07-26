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

    public OnboardingService(
        IAccountStore accounts,
        ISecretStore secrets,
        SteamAccountLocator locator,
        SteamCommunityClient community,
        Func<string, SteamApiClient> clientFactory)
    {
        _accounts = accounts;
        _secrets = secrets;
        _locator = locator;
        _community = community;
        _clientFactory = clientFactory;
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

        Changed?.Invoke();
    }

    public async Task<KeySubmission> SubmitKeyAsync(string pasted, CancellationToken cancellationToken)
    {
        // Format first, so a typo costs nothing.
        if (!ApiKey.TryNormalize(pasted, out var normalized))
        {
            return KeySubmission.Malformed;
        }

        var account = _accounts.Current
            ?? throw new InvalidOperationException("An account must be chosen before a key can be checked.");

        try
        {
            // One cheap call that answers "is this key any good". Without it the
            // user finds out several minutes into their first sync instead.
            await _clientFactory(normalized).GetOwnedGamesAsync(account.SteamId64, cancellationToken);
        }
        catch (SteamApiException e) when (e.Kind == SteamApiErrorKind.InvalidKey)
        {
            return KeySubmission.Rejected;
        }
        catch (SteamApiException)
        {
            // Rate limited, a 5xx, or a transport failure the client already
            // folded into ServerError. The key may well be fine, so it is not
            // called rejected and it is not stored either.
            return KeySubmission.Unreachable;
        }

        _secrets.Write(normalized);

        // A new key deserves a clean slate: whatever made the previous one look
        // rejected has nothing to do with this one.
        _accounts.ClearKeyRejected();

        Changed?.Invoke();
        return KeySubmission.Accepted;
    }
}
