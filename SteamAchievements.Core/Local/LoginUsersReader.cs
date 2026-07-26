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

            DateTimeOffset timestamp;
            if (long.TryParse(node["Timestamp"].Value, out var timestampSeconds))
            {
                try
                {
                    timestamp = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Clamp out-of-range timestamps to MinValue (sentinel for "unknown")
                    timestamp = DateTimeOffset.MinValue;
                }
            }
            else
            {
                // Clamp unparseable timestamps to MinValue (sentinel for "unknown")
                timestamp = DateTimeOffset.MinValue;
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
