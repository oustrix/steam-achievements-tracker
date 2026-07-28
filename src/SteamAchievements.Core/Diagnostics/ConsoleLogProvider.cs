namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// The console sink for <c>SteamAchievements.Cli</c> — format- and
/// scrub-identical to <see cref="RollingFileLoggerProvider"/> by
/// construction, since both derive from <see cref="TextLoggerProvider"/>,
/// rather than by two authors keeping two copies of "format, then scrub" in
/// step by hand.
///
/// This exists because the stock <c>Microsoft.Extensions.Logging.Console</c>
/// provider (<c>AddSimpleConsole</c>) does not scrub, and the CLI is exactly
/// the host where that matters most: it holds a real Steam Web API key
/// (typed on the command line or read from <c>STEAM_API_KEY</c>), and
/// <see cref="LoggingHandler"/> logs every request URL at Debug — which
/// carries that key in its query string. Routed through the stock provider,
/// the key would land in terminal scrollback, in any redirected log file, and
/// in CI output on every request. Routed through this one, the same
/// <see cref="Redaction.Scrub"/> call the file sink uses runs first.
/// </summary>
public sealed class ConsoleLogProvider : TextLoggerProvider
{
    private readonly TextWriter _console;

    /// <summary>
    /// Guards <see cref="_console"/> rather than trusting it to serialize its
    /// own writes. <see cref="Console.Out"/> happens to be thread-safe on its
    /// own, but the internal constructor below exists so tests can substitute
    /// a plain <see cref="StringWriter"/>, which is not — and
    /// <c>SyncOrchestrator</c> logs from concurrent HTTP work, so an
    /// unsynchronized writer would interleave two lines' bytes instead of
    /// ordering them one after another.
    /// </summary>
    private readonly Lock _gate = new();

    public ConsoleLogProvider(Func<DateTimeOffset> now) : this(Console.Out, now)
    {
    }

    /// <summary>
    /// Internal rather than a public constructor overload: production only
    /// ever wants <see cref="Console.Out"/>, and a public seam for "write
    /// somewhere else" would invite exactly the kind of second, undiscovered
    /// sink this class exists to prevent. Tests reach it via this project's
    /// existing <c>InternalsVisibleTo</c> grant to
    /// <c>SteamAchievements.Core.Tests</c>.
    /// </summary>
    internal ConsoleLogProvider(TextWriter console, Func<DateTimeOffset> now) : base(now)
    {
        _console = console;
    }

    protected override void WriteLine(string line)
    {
        lock (_gate)
        {
            _console.Write(line);
        }
    }
}
