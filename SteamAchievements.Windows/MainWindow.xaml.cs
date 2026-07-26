using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using SteamAchievements.Core.App;

namespace SteamAchievements.Windows;

public partial class MainWindow : Window
{
    private readonly HostStartup _startup;
    private string? _placardTarget;

    public MainWindow(HostStartup startup)
    {
        _startup = startup;
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // No services means composition failed, which always carries a message —
        // but coalescing here rather than asserting it keeps "there is nothing
        // to show the user" from being reachable at all.
        var failure = _startup.Services is null
            ? _startup.FailureMessage ?? "The application could not start."
            : null;

        // The decision and its wording live in Core, under dotnet test. All this
        // window contributes is the one question that genuinely needs Windows.
        var placard = HostStartupDecision.Evaluate(
            failure, WebView2Probe.IsRuntimeInstalled(), _startup.DataFolder);

        if (placard is not null)
        {
            ShowPlacard(placard);
            return;
        }

        var view = new BlazorWebView
        {
            HostPage = "wwwroot/index.html",
            Services = _startup.Services!,
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

    private void ShowPlacard(HostPlacard placard)
    {
        PlaceholderMessage.Text = placard.Message;
        PlaceholderAction.Content = placard.ActionLabel;
        PlaceholderAction.Visibility = Visibility.Visible;
        _placardTarget = placard.ActionTarget;
    }

    private void OnPlaceholderAction(object sender, RoutedEventArgs e)
    {
        if (_placardTarget is not null)
        {
            _startup.Links.OpenUrl(_placardTarget);
        }
    }
}
