using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Diagnostics;
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

// Debug, because this tool exists for the runs that go wrong. ConsoleLogProvider,
// not the stock AddSimpleConsole: this process holds a real Steam Web API key
// (from --key or STEAM_API_KEY), LoggingHandler below logs every request URL
// at Debug, and that URL carries the key in its query string. The stock
// console provider formats but does not scrub, which would print the key to
// stdout on every request; ConsoleLogProvider runs it through the same
// Redaction.Scrub the file sink uses, by construction rather than by remembering.
using var loggers = LoggerFactory.Create(builder => builder
    .SetMinimumLevel(LogLevel.Debug)
    .AddProvider(new ConsoleLogProvider(() => DateTimeOffset.UtcNow)));

using var connection = Database.Open(dbPath);
var repository = new GameRepository(connection);

// Chain: innerHandler does the real HTTP work; countingHandler wraps it to
// track request volume cheaply; loggingHandler wraps that to record every
// request; fixtureHandler (optional) wraps that to capture anonymized
// bodies. HttpClient owns and disposes the whole chain — DelegatingHandler.
// Dispose cascades to InnerHandler, so only the outermost handler needs to
// reach the HttpClient constructor.
var innerHandler = new HttpClientHandler();
var countingHandler = new RequestCountingHandler(innerHandler);

// Outside RequestCountingHandler (rather than inside it) so the elapsed time
// LoggingHandler measures — it brackets its Stopwatch around base.SendAsync —
// covers everything beneath it in the chain, including RequestCountingHandler's
// own bookkeeping, not just the network hop underneath that. RequestCountingHandler
// has no retry logic of its own to worry about here; it only increments a
// counter once per SendAsync call.
var loggingHandler = new LoggingHandler(loggers.CreateLogger<LoggingHandler>())
{
    InnerHandler = countingHandler,
};

HttpMessageHandler outermostHandler = loggingHandler;

if (options.DumpFixturesDir is not null)
{
    outermostHandler = new FixtureCapturingHandler(loggingHandler, options.DumpFixturesDir, apiKey, steamId.Value.ToString());
    Console.WriteLine($"Dumping anonymized fixtures to: {options.DumpFixturesDir}");
}

using var http = new HttpClient(outermostHandler)
{
    BaseAddress = new Uri("https://api.steampowered.com/"),
};

var client = new SteamApiClient(http, apiKey);

var orchestrator = new SyncOrchestrator(
    client, repository, SyncOptions.Default,
    loggers.CreateLogger<SyncOrchestrator>());

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
    // Scrubbed like every line ConsoleLogProvider writes. SteamApiClient never
    // echoes a request URL into an exception message by design — there is no
    // live leak today — but that is a discipline, not a guarantee, and this
    // path is the one place that prints an exception message without going
    // through TextLoggerProvider's shared format-then-scrub sequence.
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Steam rejected the API key: {Redaction.Scrub(e.Message)}");
    Console.Error.WriteLine("Check it at https://steamcommunity.com/dev/apikey, or pass a different one via --key / STEAM_API_KEY.");
    return 1;
}
catch (SteamApiException e)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Steam API error: {Redaction.Scrub(e.Message)}");
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
