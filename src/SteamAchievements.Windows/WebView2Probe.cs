using Microsoft.Web.WebView2.Core;

namespace SteamAchievements.Windows;

/// <summary>
/// Without the Evergreen runtime a BlazorWebView shows an empty window, which is
/// an unacceptable default answer. Asked before the view is constructed.
/// </summary>
public static class WebView2Probe
{
    /// <summary>
    /// The installed runtime version, or null when there is none. Callers that
    /// only need the yes-or-no answer use <see cref="IsRuntimeInstalled"/>;
    /// the log wants the version, because "an old Evergreen build" and "no
    /// runtime at all" are different problems with different fixes.
    /// </summary>
    public static string? Version()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();

            return string.IsNullOrEmpty(version) ? null : version;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return null;
        }
    }

    public static bool IsRuntimeInstalled() => Version() is not null;
}
