using Microsoft.Extensions.Logging;

namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// Shared skeleton for every text-based <see cref="ILoggerProvider"/> in this
/// application: format the event with <see cref="LogLine.Format"/>, then
/// scrub it with <see cref="Redaction.Scrub"/>, in that order, exactly once.
///
/// Extracted after a review found <c>SteamAchievements.Cli</c> wiring the
/// stock <c>Microsoft.Extensions.Logging.Console</c> provider
/// (<c>AddSimpleConsole</c>) alongside <see cref="RollingFileLoggerProvider"/>.
/// That provider formats but does not scrub, so a Steam Web API key — present
/// in the query string of every request <see cref="LoggingHandler"/> logs —
/// would have reached the terminal, and therefore terminal scrollback and any
/// redirected log file, unredacted. <see cref="Redaction.Scrub"/> had exactly
/// one caller before this fix; adding a second copy of "format, then scrub"
/// for the console sink would only have traded one caller to remember for
/// two. Instead this base class owns that sequence once. A subclass supplies
/// only the destination for the finished, already-scrubbed line, so a third
/// sink inherits the invariant by construction rather than by remembering it.
/// </summary>
public abstract class TextLoggerProvider : ILoggerProvider
{
    private readonly Func<DateTimeOffset> _now;

    /// <summary>
    /// Latched, never cleared, once <see cref="Dispose"/> runs. A hosted
    /// <c>ILoggerFactory</c> disposing its providers while another component
    /// still holds an injected <c>ILogger&lt;T&gt;</c> and logs from its own
    /// teardown is an ordinary .NET shutdown path — in the WPF host,
    /// <c>App.OnExit</c> disposes the service provider (which disposes
    /// <c>SyncCoordinator</c>, which logs) before it disposes the logger
    /// factory. Without this flag a subclass writing to a resource it has
    /// already released (a closed file stream, in
    /// <see cref="RollingFileLoggerProvider"/>'s case) could resurrect it, or
    /// simply write somewhere the process no longer expects output to go. A
    /// line lost after shutdown has begun is the correct trade — the sink
    /// must stay closed once closing has started.
    /// </summary>
    private volatile bool _disposed;

    protected TextLoggerProvider(Func<DateTimeOffset> now) => _now = now;

    public ILogger CreateLogger(string categoryName) => new TextLogger(this, categoryName);

    /// <summary>
    /// Receives one fully formatted, already-scrubbed line — including its
    /// trailing newline and any indented exception block. Never called once
    /// <see cref="_disposed"/> is set.
    /// </summary>
    protected abstract void WriteLine(string line);

    /// <summary>
    /// Redaction happens here, on the whole formatted line including the
    /// exception block, rather than at the call sites — a scrubber you have
    /// to remember to call is a scrubber that leaks.
    ///
    /// A no-op once disposed, silently, for the same reason a subclass's own
    /// writer might disable itself silently on an I/O error: logging must
    /// never be the reason something fails, and a caller logging from its own
    /// teardown after the provider is gone should not be punished for it.
    /// </summary>
    private void Write(LogLevel level, string category, string message, Exception? error)
    {
        if (_disposed)
        {
            return;
        }

        WriteLine(Redaction.Scrub(LogLine.Format(_now(), level, category, message, error)));
    }

    public virtual void Dispose() => _disposed = true;

    private sealed class TextLogger : ILogger
    {
        private readonly TextLoggerProvider _provider;
        private readonly string _category;

        public TextLogger(TextLoggerProvider provider, string category)
        {
            _provider = provider;

            // Shortened once here rather than on every line: the category is
            // bound for the lifetime of this logger instance, so re-deriving
            // its last segment on every Log call — as LogLine.Format used to —
            // repeated the same string operation for no reason.
            _category = LogLine.ShortCategory(category);
        }

        /// <summary>
        /// Scopes are not supported. Nothing in this application nests work in
        /// a way a scope would clarify, and a no-op is honest about that.
        /// </summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            _provider.Write(logLevel, _category, formatter(state, exception), exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        private NullScope()
        {
        }

        public void Dispose()
        {
        }
    }
}
