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
    /// </summary>
    private void Write(LogLevel level, string category, string message, Exception? error) =>
        _writer.Write(Redaction.Scrub(LogLine.Format(_now(), level, category, message, error)));

    public void Dispose() => _writer.Dispose();

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
