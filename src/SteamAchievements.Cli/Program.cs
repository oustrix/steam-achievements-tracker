using System.Diagnostics;
using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Local;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Sync;
using SteamAchievements.Cli;

CliOptions options;
try
{
    options = CliOptions.Parse(args);
}
catch (CliArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

var apiKey = options.ApiKey ?? Environment.GetEnvironmentVariable("STEAM_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("No Steam Web API key found.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Get one at https://steamcommunity.com/dev/apikey, then either:");
    Console.Error.WriteLine("  export STEAM_API_KEY=<your key>");
    Console.Error.WriteLine("or pass it directly:");
    Console.Error.WriteLine("  --key <your key>");
    return 1;
}

var steamId = options.SteamId ?? AutoDetectSteamId();
if (steamId is null)
{
    Console.Error.WriteLine("Could not determine a SteamID64 automatically.");
    Console.Error.WriteLine("No local Steam installation was found (or it has no logged-in accounts).");
    Console.Error.WriteLine("Pass one explicitly: --steamid <your SteamID64>");
    return 1;
}

var dbPath = options.DbPath ?? Path.Combine(Path.GetTempPath(), "steam-achievements-tracker.db");
Console.WriteLine($"Database: {dbPath}");

using var connection = Database.Open(dbPath);
var repository = new GameRepository(connection);

// Chain: innerHandler does the real HTTP work; countingHandler wraps it to
// track request volume cheaply; fixtureHandler (optional) wraps that to
// capture anonymized bodies. HttpClient owns and disposes the whole chain —
// DelegatingHandler.Dispose cascades to InnerHandler, so only the outermost
// handler needs to reach the HttpClient constructor.
var innerHandler = new HttpClientHandler();
var countingHandler = new RequestCountingHandler(innerHandler);
HttpMessageHandler outermostHandler = countingHandler;

if (options.DumpFixturesDir is not null)
{
    outermostHandler = new FixtureCapturingHandler(countingHandler, options.DumpFixturesDir, apiKey, steamId.Value.ToString());
    Console.WriteLine($"Dumping anonymized fixtures to: {options.DumpFixturesDir}");
}

using var http = new HttpClient(outermostHandler)
{
    BaseAddress = new Uri("https://api.steampowered.com/"),
};

var client = new SteamApiClient(http, apiKey);

// A real logger factory arrives with the rest of the CLI's diagnostics wiring;
// until then, a null logger keeps this compiling without silently becoming the
// production default (the constructor parameter itself is still required).
var orchestrator = new SyncOrchestrator(
    client, repository, SyncOptions.Default,
    Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncOrchestrator>.Instance);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // Keep the process alive so the in-flight batch can finish writing to
    // the database; that per-game commit (SyncOrchestrator.MarkSynced) is
    // exactly what makes an interrupted sync resumable on the next run.
    e.Cancel = true;
    Console.Error.WriteLine();
    Console.Error.WriteLine("Cancellation requested — finishing in-flight requests, then stopping...");
    cts.Cancel();
};

var progressLock = new Lock();
var lastProgressWrite = DateTime.MinValue;
var progress = new Progress<SyncProgress>(p =>
{
    lock (progressLock)
    {
        var now = DateTime.UtcNow;
        var done = p.Completed == p.Total;
        if (!done && now - lastProgressWrite < TimeSpan.FromMilliseconds(200))
        {
            return;
        }

        lastProgressWrite = now;
        var percent = p.Total == 0 ? 100 : 100.0 * p.Completed / p.Total;
        var line = $"Syncing... {p.Completed}/{p.Total} ({percent,5:F1}%) {TruncateGameName(p.CurrentGame)}";

        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(line);
        }
        else
        {
            var width = SafeWindowWidth();
            Console.Write("\r" + line.PadRight(Math.Max(line.Length, width)));
            if (done)
            {
                Console.WriteLine();
            }
        }
    }
});

var stopwatch = Stopwatch.StartNew();
var interrupted = false;

try
{
    await orchestrator.RunAsync(steamId.Value, options.Force, progress, cts.Token);
}
catch (OperationCanceledException)
{
    interrupted = true;
    Console.WriteLine();
    Console.WriteLine("Sync interrupted. Progress made so far is committed to the database;");
    Console.WriteLine($"rerun with --db {dbPath} to resume where it left off.");
}
catch (SteamApiException e) when (e.Kind == SteamApiErrorKind.InvalidKey)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Steam rejected the API key: {e.Message}");
    Console.Error.WriteLine("Check it at https://steamcommunity.com/dev/apikey, or pass a different one via --key / STEAM_API_KEY.");
    return 1;
}
catch (SteamApiException e)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Steam API error: {e.Message}");
    return 1;
}

stopwatch.Stop();

ReportPrinter.Print(repository, options.Top, countingHandler.Count, stopwatch.Elapsed);

return interrupted ? 130 : 0;

ulong? AutoDetectSteamId()
{
    ISteamPathProvider pathProvider = new CliSteamPathProvider();
    var steamPath = pathProvider.FindSteamPath();
    if (steamPath is null)
    {
        return null;
    }

    var loginUsersPath = Path.Combine(steamPath, "config", "loginusers.vdf");
    if (!File.Exists(loginUsersPath))
    {
        return null;
    }

    var accounts = LoginUsersReader.Read(File.ReadAllText(loginUsersPath));
    return LoginUsersReader.SelectActive(accounts)?.SteamId64;
}

static string TruncateGameName(string name)
{
    const int width = 40;
    if (string.IsNullOrEmpty(name))
    {
        return string.Empty;
    }

    return name.Length <= width ? name : name[..(width - 1)] + "…";
}

static int SafeWindowWidth()
{
    try
    {
        return Console.WindowWidth > 0 ? Console.WindowWidth - 1 : 80;
    }
    catch (IOException)
    {
        return 80;
    }
}
