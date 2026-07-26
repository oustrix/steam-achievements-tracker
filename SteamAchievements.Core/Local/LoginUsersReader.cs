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

            _ = long.TryParse(node["Timestamp"].Value, out var timestamp);

            accounts.Add(new SteamAccount(
                steamId,
                node["AccountName"].Value ?? string.Empty,
                node["PersonaName"].Value ?? string.Empty,
                node["MostRecent"].Value == "1",
                DateTimeOffset.FromUnixTimeSeconds(timestamp)));
        }

        return accounts;
    }

    public static SteamAccount? SelectActive(IReadOnlyList<SteamAccount> accounts) =>
        accounts.FirstOrDefault(a => a.MostRecent)
        ?? accounts.MaxBy(a => a.Timestamp);
}
