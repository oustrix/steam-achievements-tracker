using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// Turns one logged event into the text that goes in the file. Pure, so the
/// tests can assert exact output rather than a shape.
/// </summary>
public static class LogLine
{
    /// <summary>
    /// Hardcoded rather than <see cref="Environment.NewLine"/>. The file is
    /// read on Windows, where tools still expect CRLF, and the tests run on
    /// macOS and Linux — an ambient newline would make the expected output
    /// depend on the host.
    /// </summary>
    private const string NewLine = "\r\n";

    private const string Indent = "    ";

    public static string Format(
        DateTimeOffset at, LogLevel level, string category, string message, Exception? error)
    {
        var builder = new StringBuilder();

        builder.Append(at.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        builder.Append("Z  ");
        builder.Append(Abbreviate(level));
        builder.Append("  ");
        builder.Append(ShortCategory(category));
        builder.Append("  ");
        builder.Append(message);
        builder.Append(NewLine);

        if (error is not null)
        {
            // Indented so the block is visibly subordinate to its line, and so
            // a search for a category still finds the event rather than a
            // stack frame that happens to mention it.
            foreach (var line in error.ToString().Split('\n'))
            {
                builder.Append(Indent).Append(line.TrimEnd('\r')).Append(NewLine);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// <c>SteamAchievements.Core.App.SyncCoordinator</c> becomes
    /// <c>SyncCoordinator</c>. The namespace is noise once every line carries
    /// it.
    /// </summary>
    public static string ShortCategory(string category)
    {
        var lastDot = category.LastIndexOf('.');

        return lastDot < 0 ? category : category[(lastDot + 1)..];
    }

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };
}
