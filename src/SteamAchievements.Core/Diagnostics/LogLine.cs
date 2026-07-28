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

    /// <summary>
    /// A <c>message</c> containing an embedded newline is not special-cased:
    /// it becomes several physical lines in the file. That is caller
    /// discipline, not something this method enforces.
    /// </summary>
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
            //
            // Trimmed before splitting, not filtered after: an exception whose
            // Message ends in a newline (reachable when it was constructed but
            // never thrown, so no stack trace follows) would otherwise leave a
            // trailing "    \r\n" line — four spaces of indent and nothing
            // else. Trimming only the trailing newline, rather than discarding
            // every empty line Split produces, keeps a genuine blank line in
            // the middle of a message intact.
            foreach (var line in error.ToString().TrimEnd('\r', '\n').Split('\n'))
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
    ///
    /// Assumes a plain namespaced type name, e.g. what
    /// <c>ILogger&lt;SyncCoordinator&gt;</c> reports as its category in this
    /// application. It is not built to unpack a bracketed, assembly-qualified
    /// generic type name — nothing in this codebase logs through a generic
    /// category, so there is nothing to verify that shape against.
    ///
    /// An empty string has no last dot and passes through unchanged. A
    /// category ending in a dot (e.g. <c>"Foo."</c>) would otherwise shorten
    /// to an empty segment, which would print as a blank field between two
    /// spaces — worse than showing the malformed name as-is — so the whole
    /// input is returned instead.
    /// </summary>
    public static string ShortCategory(string category)
    {
        var lastDot = category.LastIndexOf('.');

        if (lastDot < 0)
        {
            return category;
        }

        var segment = category[(lastDot + 1)..];

        return segment.Length == 0 ? category : segment;
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
