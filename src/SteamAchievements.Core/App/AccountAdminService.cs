using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.App;

/// <summary>
/// Switching accounts and resetting are the same database operation — no table
/// carries a SteamID column, so a library belongs to exactly one account. The
/// difference is visible here rather than hidden in a flag: switching keeps the
/// key, resetting discards it.
/// </summary>
public sealed class AccountAdminService : IAccountAdmin
{
    private readonly ILibraryReset _reset;
    private readonly IAccountStore _accounts;
    private readonly ISecretStore _secrets;
    private readonly SteamAccountLocator _locator;
    private readonly SteamCommunityClient _community;

    public AccountAdminService(
        ILibraryReset reset,
        IAccountStore accounts,
        ISecretStore secrets,
        SteamAccountLocator locator,
        SteamCommunityClient community)
    {
        _reset = reset;
        _accounts = accounts;
        _secrets = secrets;
        _locator = locator;
        _community = community;
    }

    public StoredAccount? Current => _accounts.Current;

    public AccountMismatch? Mismatch
    {
        get
        {
            var active = _locator.FindActiveAccount();
            var stored = _accounts.Current;

            if (active is null || stored is null || active.SteamId64 == stored.SteamId64)
            {
                return null;
            }

            return new AccountMismatch(active.SteamId64, active.AccountName);
        }
    }

    public event Action? Changed;

    public async Task SwitchToAsync(ulong steamId64, CancellationToken cancellationToken)
    {
        var profile = await _community.GetProfileAsync(steamId64, cancellationToken);

        _reset.Reset();
        _accounts.Set(steamId64, profile?.PersonaName ?? string.Empty, profile?.AvatarUrl ?? string.Empty);

        Changed?.Invoke();
    }

    public void ResetEverything()
    {
        _reset.Reset();
        _secrets.Clear();

        Changed?.Invoke();
    }
}
