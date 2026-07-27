using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SteamAchievements.Core.Abstractions;
using SteamAchievements.Core.App;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Presentation;
using SteamAchievements.Core.Steam;
using SteamAchievements.Core.Sync;
using SteamAchievements.UI.State;

namespace SteamAchievements.Windows;

public partial class App : Application
{
    private ServiceProvider? _services;
    private readonly List<SqliteConnection> _connections = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = DataPaths.Resolve(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        // Built before composition, and therefore still available to the
        // failure screen if composition throws. Opening a URL depends on
        // nothing that can fail here.
        var links = new ShellLinks(paths.Folder);

        HostStartup startup;

        try
        {
            paths.EnsureFolderExists();
            startup = Compose(paths, links);
        }
        catch (Exception failure) when (
            failure is SqliteException or IOException or UnauthorizedAccessException)
        {
            startup = new HostStartup(null, OnboardingState.QueueRoute, failure.Message, paths.Folder, links);
        }

        MainWindow = new MainWindow(startup);
        MainWindow.Show();
    }

    private HostStartup Compose(DataPaths paths, IExternalLinks links)
    {
        // The order of these three is not free. Database.OpenRead deliberately
        // does not migrate — schema ownership belongs to the writer — so opening
        // the reader first makes GetSummary fail on a missing settings table on
        // a clean machine. Do not reorder.
        var writer = Track(Database.Open(paths.DatabaseFile));
        var reader = Track(Database.OpenRead(paths.DatabaseFile));
        var settings = Track(Database.OpenSettings(paths.DatabaseFile));

        var secrets = new DpapiSecretStore(paths.SecretFile);
        var accounts = new SqliteAccountStore(settings);
        var journal = new SyncJournal(settings);
        var locator = new SteamAccountLocator(new RegistrySteamPathProvider());
        var community = new SteamCommunityClient(
            new HttpClient { BaseAddress = new Uri("https://steamcommunity.com/") });

        var services = new ServiceCollection();

        // One window, one session: everything the preview host registers as
        // Scoped is a singleton here.
        services.AddSingleton<ISecretStore>(secrets);
        services.AddSingleton<IAccountStore>(accounts);
        services.AddSingleton(journal);
        services.AddSingleton(locator);
        services.AddSingleton(community);
        services.AddSingleton(links);
        services.AddSingleton<ILibraryReset>(new SqliteLibraryReset(settings));

        services.AddSingleton<ILibraryQuery>(new SqliteLibraryQuery(reader));
        services.AddSingleton<IUserPreferences>(new SqliteUserPreferences(settings));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<QueueState>();

        // One HttpClient for the process, a new SteamApiClient per key. The key
        // is a constructor argument of the client, not of the transport, so
        // sharing the transport still lets a replaced key take effect on the
        // next sync — and avoids a fresh TCP and TLS handshake each time.
        var steamApi = new HttpClient { BaseAddress = new Uri("https://api.steampowered.com/") };
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
        // it from the coordinator.
        _ = _services.GetRequiredService<LibraryChangeSignal>();

        // Asked, not recomputed. Re-deriving the step here would put the rule in
        // two places, and the copy would be the one that cannot be unit-tested.
        var step = _services.GetRequiredService<IOnboarding>().Step;

        return new HostStartup(_services, OnboardingState.RouteFor(step), null, paths.Folder, links);
    }

    private SqliteConnection Track(SqliteConnection connection)
    {
        _connections.Add(connection);
        return connection;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Order matters as much as it did on the way in: the sync has to be
        // stopped and awaited before the connections it writes through are
        // closed, or the orchestrator's worker pool writes into disposed
        // handles. Disposing the provider is what stops it — the coordinator is
        // a singleton the container owns — so the connection loop comes after.
        _services?.Dispose();

        foreach (var connection in _connections)
        {
            connection.Dispose();
        }

        base.OnExit(e);
    }
}
