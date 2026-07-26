namespace SteamAchievements.Cli;

/// <summary>Thrown for a malformed command line; caught in Program.Main and printed without a stack trace.</summary>
public sealed class CliArgumentException(string message) : Exception(message);

public sealed record CliOptions(
    string? ApiKey,
    ulong? SteamId,
    string? DbPath,
    bool Force,
    int Top,
    string? DumpFixturesDir)
{
    public const string HelpText = """
        steam-achievements-cli — exercises SteamAchievements.Core against the real Steam Web API.

        Usage:
          steam-achievements-cli [options]

        Options:
          --key <value>            Steam Web API key. Falls back to the STEAM_API_KEY
                                    environment variable. Get one at
                                    https://steamcommunity.com/dev/apikey
          --steamid <id64>         SteamID64 to sync. Auto-detected from the local Steam
                                    installation's loginusers.vdf when omitted.
          --db <path>              SQLite database path. Defaults to a file in the OS
                                    temp directory.
          --force                  Bypass the incremental TTL logic and refresh everything.
          --top <n>                Number of games to show in the report (default 20).
          --dump-fixtures <dir>    Write one anonymized raw JSON response per endpoint into
                                    <dir>, for replacing synthetic test fixtures with real ones.
          --help, -h               Show this help text.
        """;

    public static CliOptions Parse(string[] args)
    {
        string? key = null;
        ulong? steamId = null;
        string? db = null;
        var force = false;
        var top = 20;
        string? dumpFixtures = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--key":
                    key = RequireValue(args, ref i, "--key");
                    break;

                case "--steamid":
                    var rawSteamId = RequireValue(args, ref i, "--steamid");
                    if (!ulong.TryParse(rawSteamId, out var parsedSteamId))
                    {
                        throw new CliArgumentException($"--steamid value '{rawSteamId}' is not a valid SteamID64.");
                    }

                    steamId = parsedSteamId;
                    break;

                case "--db":
                    db = RequireValue(args, ref i, "--db");
                    break;

                case "--force":
                    force = true;
                    break;

                case "--top":
                    var rawTop = RequireValue(args, ref i, "--top");
                    if (!int.TryParse(rawTop, out top) || top <= 0)
                    {
                        throw new CliArgumentException($"--top value '{rawTop}' must be a positive integer.");
                    }

                    break;

                case "--dump-fixtures":
                    dumpFixtures = RequireValue(args, ref i, "--dump-fixtures");
                    break;

                case "--help":
                case "-h":
                    Console.WriteLine(HelpText);
                    Environment.Exit(0);
                    break;

                default:
                    throw new CliArgumentException($"Unknown argument: {args[i]}. Run --help for usage.");
            }
        }

        return new CliOptions(key, steamId, db, force, top, dumpFixtures);
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
        {
            throw new CliArgumentException($"{flag} requires a value.");
        }

        i++;
        return args[i];
    }
}
