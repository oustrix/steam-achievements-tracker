using Microsoft.Extensions.Logging;

namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// The file sink, as an <see cref="ILoggerProvider"/> so that call sites can
/// take the conventional <c>ILogger&lt;T&gt;</c>.
///
/// Nothing is filtered: the application has never started on Windows, and the
/// first failure has to be in the file already rather than reproducible on a
/// second run with a flag set. Raising the floor later is a one-line change in
/// the host.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly RollingFileWriter _writer;
    private readonly Func<DateTimeOffset> _now;

    /// <summary>
    /// Latched, never cleared, once <see cref="Dispose"/> runs. A hosted
    /// <c>ILoggerFactory</c> disposing its providers while another component
    /// still holds an injected <c>ILogger&lt;T&gt;</c> and logs from its own
    /// teardown is an ordinary .NET shutdown path — in this application,
    /// <c>App.OnExit</c> disposes the service provider (which disposes
    /// <c>SyncCoordinator</c>, which logs) before it disposes the logger
    /// factory. Without this flag, <see cref="RollingFileWriter.Write"/> would
    /// see its stream is <see langword="null"/> and transparently reopen the
    /// file, resurrecting a log the process believes it has already closed and
    /// leaving a stray handle open after shutdown. A line lost after shutdown
    /// has begun is the correct trade — the file must stay closed once
    /// closing has started.
    /// </summary>
    private volatile bool _disposed;

    /// <param name="now">
    /// Injected rather than read ambiently, which is the convention everywhere
    /// else in Core and is what lets the formatting tests assert exact output.
    /// </param>
    public RollingFileLoggerProvider(LogFileOptions options, Func<DateTimeOffset> now)
    {
        _writer = new RollingFileWriter(options);
        _now = now;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <summary>
    /// Redaction happens here, on the whole formatted line including the
    /// exception block, rather than at the call sites — a scrubber you have to
    /// remember to call is a scrubber that leaks.
    ///
    /// A no-op once disposed, silently, for the same reason
    /// <see cref="RollingFileWriter"/> disables itself silently on an I/O
    /// error: logging must never be the reason something fails, and a caller
    /// logging from its own teardown after the provider is gone should not be
    /// punished for it.
    /// </summary>
    private void Write(LogLevel level, string category, string message, Exception? error)
    {
        if (_disposed)
        {
            return;
        }

        _writer.Write(Redaction.Scrub(LogLine.Format(_now(), level, category, message, error)));
    }

    public void Dispose()
    {
        _disposed = true;
        _writer.Dispose();
    }

    private sealed class FileLogger : ILogger
    {
        private readonly RollingFileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(RollingFileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
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
