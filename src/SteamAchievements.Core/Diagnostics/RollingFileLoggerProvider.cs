namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// The file sink. Formatting, scrubbing, the <c>ILogger</c>/<c>ILoggerProvider</c>
/// boilerplate, and the disposed-latch all live in <see cref="TextLoggerProvider"/>;
/// this class only owns the rotating file underneath it.
///
/// Nothing is filtered: the application has never started on Windows, and the
/// first failure has to be in the file already rather than reproducible on a
/// second run with a flag set. Raising the floor later is a one-line change in
/// the host.
/// </summary>
public sealed class RollingFileLoggerProvider : TextLoggerProvider
{
    private readonly RollingFileWriter _writer;

    /// <param name="now">
    /// Injected rather than read ambiently, which is the convention everywhere
    /// else in Core and is what lets the formatting tests assert exact output.
    /// </param>
    public RollingFileLoggerProvider(LogFileOptions options, Func<DateTimeOffset> now) : base(now)
    {
        _writer = new RollingFileWriter(options);
    }

    protected override void WriteLine(string line) => _writer.Write(line);

    public override void Dispose()
    {
        base.Dispose();
        _writer.Dispose();
    }
}
