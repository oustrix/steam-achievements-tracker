namespace SteamAchievements.Core.Local;

public static class LoginUsersReader
{
    public static IReadOnlyList<SteamAccount> Read(string vdfText)
    {
        var users = VdfParser.Parse(vdfText)["users"];
        var accounts = new List<SteamAccount>();

        foreach (var (rawId, node) in users.Children)
        {
            if (!ulong.TryParse(rawId, out var steamId))
            {
                continue;
            }

            // Unparseable or out-of-range timestamps clamp to MinValue rather
            // than being skipped: the account itself is still real and
            // usable (SelectActive can still find it via the MostRecent
            // flag), and MinValue sorts before every genuine timestamp, so a
            // corrupted entry simply loses timestamp-based tie-breaking
            // instead of vanishing from the account list entirely.
            var timestamp = DateTimeOffset.MinValue;
            if (long.TryParse(node["Timestamp"].Value, out var timestampSeconds))
            {
                try
                {
                    timestamp = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Leave timestamp at the MinValue default set above.
                }
            }

            accounts.Add(new SteamAccount(
                steamId,
                node["AccountName"].Value ?? string.Empty,
                node["PersonaName"].Value ?? string.Empty,
                node["MostRecent"].Value == "1",
                timestamp));
        }

        return accounts;
    }

    public static SteamAccount? SelectActive(IReadOnlyList<SteamAccount> accounts)
    {
        var flagged = accounts.Where(a => a.MostRecent).ToList();
        return flagged.Count > 0
            ? flagged.MaxBy(a => a.Timestamp)
            : accounts.MaxBy(a => a.Timestamp);
    }
}
