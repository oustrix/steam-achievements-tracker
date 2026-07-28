using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.App;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Diagnostics;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Sync;
using SteamAchievements.UI.State;

namespace SteamAchievements.Windows;

public partial class App : Application
{
    private ServiceProvider? _services;
    private readonly List<SqliteConnection> _connections = [];
    private ILoggerFactory? _loggers;
    private ILogger<App>? _log;
    private string _logFile = "";

    /// <summary>
    /// Guards the dialog in <see cref="InstallExceptionHooks"/> so a recurring
    /// dispatcher exception — a failing render or layout pass firing every
    /// frame — cannot reshow a blocking <c>MessageBox</c> on every tick.
    /// </summary>
    private bool _crashDialogShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = DataPaths.Resolve(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        _logFile = paths.LogFile;

        // The order below is deliberate. Resolving the paths and opening the
        // log come first, because they decide *where* anything could be
        // recorded; the only failures they have are ones that leave nowhere to
        // record them, so there is nothing to gain from a second, buffered
        // logger covering these two lines. Everything after this point is
        // logged, including the hooks themselves.
        try
        {
            paths.EnsureFolderExists();
        }
        catch (Exception folderFailure) when (
            folderFailure is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"The data folder could not be created:\n\n{paths.Folder}\n\n{folderFailure.Message}",
                "Steam Achievements Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);

            return;
        }

        _loggers = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(new RollingFileLoggerProvider(
                new LogFileOptions(paths.Folder, DataPaths.LogFileName),
                () => DateTimeOffset.UtcNow)));

        // A local rather than reading the field back: the field stays
        // nullable because OnExit genuinely can run with it unset (the early
        // Shutdown(1) return above), but everything from here to the end of
        // this method has it, so keeping it as a local of its non-nullable
        // declared type makes that structural instead of a per-call `!`.
        var log = _loggers.CreateLogger<App>();
        _log = log;

        InstallExceptionHooks(log);
        LogEnvironment(log, paths);

        // Built before composition, and therefore still available to the
        // failure screen if composition throws. Opening a URL depends on
        // nothing that can fail here.
        var links = new ShellLinks(paths.Folder, paths.LogFile);

        HostStartup startup;

        try
        {
            startup = Compose(paths, links);
        }
        catch (Exception failure) when (
            failure is SqliteException or IOException or UnauthorizedAccessException)
        {
            log.LogCritical(failure, "composition failed; showing the failure placard");
            startup = new HostStartup(null, OnboardingState.QueueRoute, failure.Message, paths.Folder, links);
        }

        MainWindow = new MainWindow(startup);
        MainWindow.Show();

        log.LogInformation("window shown, start path {StartPath}", startup.StartPath);
    }

    /// <summary>
    /// The three ways an exception escapes without any of them being visible
    /// today: a crash currently looks like a window that silently disappeared.
    /// </summary>
    /// <param name="log">
    /// Passed explicitly rather than read back from <see cref="_log"/>: every
    /// call site here runs after <see cref="OnStartup"/> has assigned it, and
    /// a parameter of the non-nullable <c>ILogger&lt;App&gt;</c> type makes
    /// that structural instead of a matter of remembering a <c>!</c> on every
    /// dereference.
    /// </param>
    private void InstallExceptionHooks(ILogger<App> log)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            log.LogCritical(args.Exception, "unhandled exception on the dispatcher thread");

            // Shown once per process, not once per exception. A dispatcher
            // exception can recur on every tick — a render or layout pass that
            // fails the same way each frame — and this MessageBox is modal on
            // the very thread that has to process the next tick; reshowing it
            // every time would leave killing the process as the only escape,
            // which is worse than the vanishing window this hook exists to
            // replace. Every occurrence is still logged at Critical above —
            // the file is the complete record — only the dialog stops
            // repeating. A second, different exception gets no dialog of its
            // own either: distinguishing "the same failure again" from "a new
            // one" would need identity comparison this handler has no basis
            // for, so once-per-process is the honest rule rather than a
            // guess at which exceptions deserve their own popup.
            if (!_crashDialogShown)
            {
                _crashDialogShown = true;

                // Scrubbed like every line that reaches log.txt. SteamApiClient
                // never echoes a request URL into an exception message by
                // design — there is no live leak today — but that is a
                // discipline elsewhere in the codebase, not a guarantee this
                // MessageBox can rely on, and unlike the log file this dialog
                // is not something the user can be asked to keep private.
                MessageBox.Show(
                    $"Something went wrong:\n\n{Redaction.Scrub(args.Exception.Message)}\n\nDetails were written to:\n{_logFile}\n\nFurther errors will be written to the log but not shown.",
                    "Steam Achievements Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            log.LogCritical(
                args.ExceptionObject as Exception,
                "unhandled exception, terminating={Terminating}", args.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            log.LogError(args.Exception, "unobserved task exception");
            args.SetObserved();
        };
    }

    private void LogEnvironment(ILogger<App> log, DataPaths paths)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

        log.LogInformation(
            "starting version={Version} os={Os} arch={Arch} process={ProcessArch}",
            version, Environment.OSVersion.VersionString,
            RuntimeInformation.OSArchitecture, RuntimeInformation.ProcessArchitecture);

        log.LogInformation(
            "webview2 runtime {Version}", WebView2Probe.Version() ?? "not installed");

        log.LogInformation(
            "folder={Folder} database={Database} exists={Exists} log={Log}",
            paths.Folder, paths.DatabaseFile, File.Exists(paths.DatabaseFile), paths.LogFile);
    }

    private HostStartup Compose(DataPaths paths, IExternalLinks links)
    {
        // The order of these three is not free. Database.OpenRead deliberately
        // does not migrate — schema ownership belongs to the writer — so opening
        // the reader first makes GetSummary fail on a missing settings table on
        // a clean machine. Do not reorder.
        var openingConnections = Stopwatch.GetTimestamp();

        var writer = Track(Database.Open(paths.DatabaseFile));
        var reader = Track(Database.OpenRead(paths.DatabaseFile));
        var settings = Track(Database.OpenSettings(paths.DatabaseFile));

        _log!.LogInformation(
            "three connections open and the schema migrated in {Elapsed}ms",
            (long)Stopwatch.GetElapsedTime(openingConnections).TotalMilliseconds);

        var secrets = new DpapiSecretStore(paths.SecretFile);
        var accounts = new SqliteAccountStore(settings);
        var journal = new SyncJournal(settings);

        // Asked here rather than inside SteamAccountLocator, which would mean
        // giving a Core type a logger for one line. "not found" is the first
        // thing to check when onboarding cannot suggest an account.
        var steamPaths = new RegistrySteamPathProvider();

        _log!.LogInformation("steam installation: {Path}", steamPaths.FindSteamPath() ?? "not found");

        var locator = new SteamAccountLocator(steamPaths);
        var community = new SteamCommunityClient(
            new HttpClient(
                new LoggingHandler(_loggers!.CreateLogger<LoggingHandler>())
                {
                    InnerHandler = new HttpClientHandler(),
                })
            {
                BaseAddress = new Uri("https://steamcommunity.com/"),
            });

        var services = new ServiceCollection();

        // Required by BlazorWebView to resolve IJSRuntime, INavigationInterception
        // and IErrorBoundaryLogger. Without it, the window fails the moment
        // MainWindow constructs the view — a placard's worth of nothing where
        // the app should be. This call appeared in no commit in this
        // repository's history until now; every Windows run before it would
        // have hit that failure at the first BlazorWebView.
        //
        // Placed first, before the application's own registrations, on
        // purpose: AddWpfBlazorWebView calls AddLogging() internally, which
        // registers ILoggerFactory and ILogger<> with TryAdd — added only if
        // nothing is registered for that service yet. Registering the
        // explicit ILoggerFactory and ILogger<> below afterwards, with plain
        // AddSingleton, still wins: a container resolves the last descriptor
        // registered for a given service, and TryAdd's registrations lose to
        // any later Add regardless of order otherwise. Confirmed by reading
        // Microsoft.AspNetCore.Components.WebView.dll — AddLogging there is
        // TryAdd/TryAddScoped throughout, never a plain Add.
        services.AddWpfBlazorWebView();

        // The components and the Core services take ILogger<T>; this is what
        // makes it resolvable. Registered from the existing factory rather than
        // through AddLogging so there is exactly one provider and one file.
        services.AddSingleton(_loggers!);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        // One window, one session: everything the preview host registers as
        // Scoped is a singleton here.
        services.AddSingleton<ISecretStore>(secrets);
        services.AddSingleton<IAccountStore>(accounts);
        services.AddSingleton(journal);
        services.AddSingleton(locator);
        services.AddSingleton(community);
        services.AddSingleton(links);
        services.AddSingleton<ILibraryReset>(
            new SqliteLibraryReset(settings, _loggers!.CreateLogger<SqliteLibraryReset>()));

        services.AddSingleton<ILibraryQuery>(new SqliteLibraryQuery(reader));
        services.AddSingleton<IUserPreferences>(new SqliteUserPreferences(settings));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<QueueState>();

        // One HttpClient for the process, a new SteamApiClient per key. The key
        // is a constructor argument of the client, not of the transport, so
        // sharing the transport still lets a replaced key take effect on the
        // next sync — and avoids a fresh TCP and TLS handshake each time.
        var steamApi = new HttpClient(
            new LoggingHandler(_loggers!.CreateLogger<LoggingHandler>())
            {
                InnerHandler = new HttpClientHandler(),
            })
        {
            BaseAddress = new Uri("https://api.steampowered.com/"),
        };
        services.AddSingleton<Func<string, SteamApiClient>>(_ => key => new SteamApiClient(steamApi, key));

        services.AddSingleton(new GameRepository(writer));
        services.AddSingleton<ISyncRunner, LiveSyncRunner>();
        services.AddSingleton<Func<DateTimeOffset>>(_ => () => DateTimeOffset.UtcNow);
        services.AddSingleton<SyncCoordinator>();
        services.AddSingleton<ISyncPresenter>(s => s.GetRequiredService<SyncCoordinator>());
        services.AddSingleton<ISyncController>(s => s.GetRequiredService<SyncCoordinator>());
        services.AddSingleton<LibraryChangeSignal>();

        services.AddSingleton<IOnboarding, OnboardingService>();
        services.AddSingleton<IAccountAdmin, AccountAdminService>();

        _services = services.BuildServiceProvider();

        // Resolved eagerly: it is a subscriber, and a lazily-created subscriber
        // misses every event raised before the first screen that injects it is
        // drawn. The container disposes it with the provider, which unsubscribes
        // it from both of its sources — the sync coordinator and the account
        // administration service.
        _ = _services.GetRequiredService<LibraryChangeSignal>();

        // Asked, not recomputed. Re-deriving the step here would put the rule in
        // two places, and the copy would be the one that cannot be unit-tested.
        var step = _services.GetRequiredService<IOnboarding>().Step;

        _log!.LogInformation("onboarding step at startup: {Step}", step);

        return new HostStartup(_services, OnboardingState.RouteFor(step), null, paths.Folder, links);
    }

    private SqliteConnection Track(SqliteConnection connection)
    {
        _connections.Add(connection);
        return connection;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.LogInformation("shutting down");

        // Order matters as much as it did on the way in: the sync has to be
        // stopped and awaited before the connections it writes through are
        // closed, or the orchestrator's worker pool writes into disposed
        // handles. Disposing the provider is what stops it — the coordinator is
        // a singleton the container owns — so the connection loop comes after.
        //
        // Each stage is also independent of the others: SyncCoordinator.Dispose
        // joins its worker loop with a bounded wait and can rethrow a faulted
        // task, and a throw there must not silently skip the connection
        // closes or the logger disposal that follows — that would lose
        // exactly the shutdown record the Windows checklist exercises by
        // closing the app mid-sync. A stage that throws is logged at Critical
        // while the logger still exists, and execution continues.
        try
        {
            _services?.Dispose();
            _log?.LogInformation("services disposed");
        }
        catch (Exception disposalFailure)
        {
            _log?.LogCritical(disposalFailure, "disposing services failed during shutdown");
        }

        try
        {
            foreach (var connection in _connections)
            {
                connection.Dispose();
            }

            _log?.LogInformation("{Count} connections closed; goodbye", _connections.Count);
        }
        catch (Exception disposalFailure)
        {
            _log?.LogCritical(disposalFailure, "closing a connection failed during shutdown");
        }

        // Last, so everything above reaches the file — regardless of whether
        // either stage above threw.
        _loggers?.Dispose();

        base.OnExit(e);
    }
}
