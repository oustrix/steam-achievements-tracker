using Microsoft.Web.WebView2.Core;

namespace SteamAchievements.Windows;

/// <summary>
/// Without the Evergreen runtime a BlazorWebView shows an empty window, which is
/// an unacceptable default answer. Asked before the view is constructed.
/// </summary>
public static class WebView2Probe
{
    public static bool IsRuntimeInstalled()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
    }
}
