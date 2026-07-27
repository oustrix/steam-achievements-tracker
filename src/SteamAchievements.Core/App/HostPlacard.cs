namespace SteamAchievements.Core.App;

/// <summary>
/// What the host window shows instead of the web view, and the one thing the
/// user can do about it. Null means "nothing is wrong — show the application".
/// </summary>
public sealed record HostPlacard(string Message, string ActionLabel, string ActionTarget);

/// <summary>
/// The startup decision: application, or an explanation of why not.
///
/// This lives in Core rather than in the window because it is policy and copy,
/// and <c>SteamAchievements.Windows</c> is verified only by a three-to-five
/// minute CI cycle. The window keeps what genuinely needs Windows — asking
/// whether the WebView2 runtime is installed — and that arrives here as a
/// <see cref="bool"/>.
/// </summary>
public static class HostStartupDecision
{
    /// <summary>
    /// Where the Evergreen bootstrapper lives. Here rather than in the host so
    /// the set of addresses the application can be told to open stays
    /// reviewable in the project that has tests.
    /// </summary>
    public const string WebView2DownloadPage = "https://developer.microsoft.com/microsoft-edge/webview2/";

    /// <param name="failureMessage">
    /// Non-null when composition itself failed — a locked or corrupt database.
    /// </param>
    /// <param name="webViewInstalled">
    /// False when the WebView2 runtime is absent. Without it a BlazorWebView
    /// renders an empty window, which is not an acceptable default answer.
    /// </param>
    /// <param name="dataFolder">Shown so the user can go look at the files themselves.</param>
    public static HostPlacard? Evaluate(string? failureMessage, bool webViewInstalled, string dataFolder)
    {
        // Ordered deliberately: a database that will not open is worth saying
        // even on a machine that also lacks WebView2, because it is the one the
        // user can act on with the files in front of them.
        if (failureMessage is not null)
        {
            return new HostPlacard(
                $"The application could not open its database.\n\n{failureMessage}\n\nData folder: {dataFolder}",
                "Open data folder",
                dataFolder);
        }

        if (!webViewInstalled)
        {
            return new HostPlacard(
                "This application needs the Microsoft Edge WebView2 runtime, which is not installed on this machine.",
                "Install WebView2",
                WebView2DownloadPage);
        }

        return null;
    }
}
