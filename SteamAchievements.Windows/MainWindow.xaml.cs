using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using SteamAchievements.Core.Presentation;

namespace SteamAchievements.Windows;

public partial class MainWindow : Window
{
    private const string WebView2Download = "https://developer.microsoft.com/microsoft-edge/webview2/";

    private readonly HostStartup _startup;
    private Action? _placeholderAction;

    public MainWindow(HostStartup startup)
    {
        _startup = startup;
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (_startup.FailureMessage is not null || _startup.Services is null)
        {
            ShowMessage(
                $"The application could not open its database.\n\n{_startup.FailureMessage}\n\nData folder: {_startup.DataFolder}",
                "Open data folder",
                () => new ShellLinks(_startup.DataFolder).OpenDataFolder());
            return;
        }

        if (!WebView2Probe.IsRuntimeInstalled())
        {
            ShowMessage(
                "This application needs the Microsoft Edge WebView2 runtime, which is not installed on this machine.",
                "Install WebView2",
                () => _startup.Services.GetRequiredService<IExternalLinks>().OpenUrl(WebView2Download));
            return;
        }

        var view = new BlazorWebView
        {
            HostPage = "wwwroot/index.html",
            Services = _startup.Services,
            StartPath = _startup.StartPath,
        };

        view.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.Routes),
        });

        // WebView2 takes hundreds of milliseconds to come up. The placeholder
        // covers exactly that gap.
        view.BlazorWebViewInitialized += (_, _) => Placeholder.Visibility = Visibility.Collapsed;

        Root.Children.Add(view);
    }

    private void ShowMessage(string message, string actionLabel, Action action)
    {
        PlaceholderMessage.Text = message;
        PlaceholderAction.Content = actionLabel;
        PlaceholderAction.Visibility = Visibility.Visible;
        _placeholderAction = action;
    }

    private void OnPlaceholderAction(object sender, RoutedEventArgs e) => _placeholderAction?.Invoke();
}
