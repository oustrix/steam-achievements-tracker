using System.Globalization;
using SteamAchievements.Core.Data;

namespace SteamAchievements.Core.Presentation;

/// <summary>
/// The account row on the settings screen. <c>SwitchPrompt</c> and
/// <c>SwitchTarget</c> are both null or both set — an offer to switch needs
/// somebody to switch to.
/// </summary>
public sealed record AccountRow(
    string Name, string Detail, string? AvatarUrl, string? SwitchPrompt, ulong? SwitchTarget);

public static class AccountRowView
{
    public const string NoAccount = "Not signed in yet";

    public static AccountRow For(StoredAccount? stored, AccountMismatch? mismatch)
    {
        var prompt = mismatch is null
            ? null
            : $"Steam is signed in as {mismatch.ActiveAccountName}";

        if (stored is null)
        {
            return new AccountRow(
                NoAccount,
                "Detected from loginusers.vdf during onboarding",
                null,
                prompt,
                mismatch?.ActiveSteamId64);
        }

        var id = stored.SteamId64.ToString(CultureInfo.InvariantCulture);
        var named = stored.PersonaName.Length > 0;

        return new AccountRow(
            named ? stored.PersonaName : id,
            named ? id : $"{id} — the public profile did not answer",
            stored.AvatarUrl.Length > 0 ? stored.AvatarUrl : null,
            prompt,
            mismatch?.ActiveSteamId64);
    }
}
