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

            if (!long.TryParse(node["Timestamp"].Value, out var timestamp))
            {
                // Skip entries with unparseable timestamps
                continue;
            }

            try
            {
                accounts.Add(new SteamAccount(
                    steamId,
                    node["AccountName"].Value ?? string.Empty,
                    node["PersonaName"].Value ?? string.Empty,
                    node["MostRecent"].Value == "1",
                    DateTimeOffset.FromUnixTimeSeconds(timestamp)));
            }
            catch (ArgumentOutOfRangeException)
            {
                // Skip entries with timestamps that cannot be converted to DateTimeOffset
                continue;
            }
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
