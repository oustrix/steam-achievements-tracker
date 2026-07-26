using SteamAchievements.Core.App;
using SteamAchievements.Core.Local;

namespace SteamAchievements.Core.Presentation;

/// <summary>How far a submitted key got. Only <c>Accepted</c> stores anything.</summary>
public enum KeySubmission
{
    /// <summary>Not 32 hexadecimal characters. No request was made.</summary>
    Malformed,

    /// <summary>Well-formed, and Steam refused it.</summary>
    Rejected,

    /// <summary>Steam could not be asked. Distinct from a refusal: retrying is the right advice.</summary>
    Unreachable,

    Accepted,
}

/// <summary>
/// What the onboarding screen talks to. <c>Changed</c> follows the same rule as
/// <see cref="ISyncPresenter.Changed"/>: wrap the reaction in
/// <c>InvokeAsync(StateHasChanged)</c>.
/// </summary>
public interface IOnboarding
{
    OnboardingStep Step { get; }

    /// <summary>Accounts signed into Steam on this machine. Empty is normal, not an error.</summary>
    IReadOnlyList<SteamAccount> DiscoveredAccounts { get; }

    /// <summary>
    /// Stores the account, filling in the persona name and avatar if the public
    /// profile answers. A profile lookup failure never blocks this.
    /// </summary>
    Task ChooseAccountAsync(ulong steamId64, CancellationToken cancellationToken);

    /// <summary>
    /// Checks the format, then checks the key against Steam, then stores it.
    /// Nothing is stored unless the result is <see cref="KeySubmission.Accepted"/>.
    /// Throws <see cref="InvalidOperationException"/> if no account has been
    /// chosen yet — that is a screen ordering bug, not a user state.
    /// </summary>
    Task<KeySubmission> SubmitKeyAsync(string pasted, CancellationToken cancellationToken);

    event Action? Changed;
}
