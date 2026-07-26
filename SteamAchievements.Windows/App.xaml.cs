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

        HostStartup startup;

        try
        {
            paths.EnsureFolderExists();
            startup = Compose(paths);
        }
        catch (Exception failure) when (
            failure is SqliteException or IOException or UnauthorizedAccessException)
        {
            startup = new HostStartup(null, "/", failure.Message, paths.Folder);
        }

        MainWindow = new MainWindow(startup);
        MainWindow.Show();
    }

    private HostStartup Compose(DataPaths paths)
    {
        // The order of these three is not free. Database.OpenRead deliberately
        // does not migrate — schema ownership belongs to the writer — so opening
        // the reader first makes GetSummary fail on a missing settings table on
        // a clean machine. Do not reorder.
        var writer = Track(Database.Open(paths.DatabaseFile));
        var reader = Track(Database.OpenRead(paths.DatabaseFile));
        var settings = Track(Database.Open(paths.DatabaseFile));

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
        services.AddSingleton<IExternalLinks>(new ShellLinks(paths.Folder));

        services.AddSingleton<ILibraryQuery>(new SqliteLibraryQuery(reader));
        services.AddSingleton<IUserPreferences>(new SqliteUserPreferences(settings));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<QueueState>();

        // A new client per key rather than one for the process: the user can
        // replace the key in settings, and the next sync has to use the new one.
        services.AddSingleton<Func<string, SteamApiClient>>(_ => key =>
            new SteamApiClient(
                new HttpClient { BaseAddress = new Uri("https://api.steampowered.com/") }, key));

        services.AddSingleton(new GameRepository(writer));
        services.AddSingleton<ISyncRunner, LiveSyncRunner>();
        services.AddSingleton<Func<DateTimeOffset>>(_ => () => DateTimeOffset.UtcNow);
        services.AddSingleton<SyncCoordinator>();
        services.AddSingleton<ISyncPresenter>(s => s.GetRequiredService<SyncCoordinator>());
        services.AddSingleton<ISyncController>(s => s.GetRequiredService<SyncCoordinator>());

        services.AddSingleton<IOnboarding, OnboardingService>();
        services.AddSingleton<IAccountAdmin>(_ => new AccountAdminService(
            settings, accounts, secrets, locator, community));

        _services = services.BuildServiceProvider();

        var step = OnboardingState.Evaluate(accounts.Current?.SteamId64, !string.IsNullOrEmpty(secrets.Read()));

        return new HostStartup(
            _services,
            step == OnboardingStep.Ready ? "/" : "/onboarding",
            null,
            paths.Folder);
    }

    private SqliteConnection Track(SqliteConnection connection)
    {
        _connections.Add(connection);
        return connection;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Order matters as much as it did on the way in. Disposing the
        // coordinator cancels the sync and waits for it; closing the connections
        // first would leave the orchestrator's worker pool writing into disposed
        // handles.
        _services?.GetService<SyncCoordinator>()?.Dispose();
        _services?.Dispose();

        foreach (var connection in _connections)
        {
            connection.Dispose();
        }

        base.OnExit(e);
    }
}
