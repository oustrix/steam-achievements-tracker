using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SteamAchievements.Core.Diagnostics;

namespace SteamAchievements.Core.Tests.Diagnostics;

public class LoggingHandlerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "sathandler-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;

        public StubHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond());
    }

    private static HttpClient ClientOver(ILogger<LoggingHandler> log, Func<HttpResponseMessage> respond) =>
        new(new LoggingHandler(log) { InnerHandler = new StubHandler(respond) });

    private static string FindLine(string path, string containing) =>
        Assert.Single(
            File.ReadAllText(path).Split("\r\n", StringSplitOptions.RemoveEmptyEntries),
            line => line.Contains(containing));

    [Fact]
    public async Task LogsTheMethodTheUrlAndTheStatus()
    {
        var provider = new RollingFileLoggerProvider(
            new LogFileOptions(_directory), () => DateTimeOffset.UnixEpoch);
        var log = new LoggerFactory([provider]).CreateLogger<LoggingHandler>();

        using var client = ClientOver(log, () => new HttpResponseMessage(HttpStatusCode.OK));
        await client.GetAsync("https://api.steampowered.com/x?steamid=7");

        provider.Dispose();
        var text = File.ReadAllText(Path.Combine(_directory, "log.txt"));

        Assert.Contains("GET", text);
        Assert.Contains("https://api.steampowered.com/x?steamid=7", text);
        Assert.Contains("200", text);
    }

    [Fact]
    public async Task NeverLetsTheKeyInAUrlReachTheFile()
    {
        var provider = new RollingFileLoggerProvider(
            new LogFileOptions(_directory), () => DateTimeOffset.UnixEpoch);
        var log = new LoggerFactory([provider]).CreateLogger<LoggingHandler>();

        using var client = ClientOver(log, () => new HttpResponseMessage(HttpStatusCode.OK));
        await client.GetAsync(
            "https://api.steampowered.com/x?key=ABCDEF0123456789ABCDEF0123456789&steamid=7");

        provider.Dispose();
        var text = File.ReadAllText(Path.Combine(_directory, "log.txt"));

        Assert.DoesNotContain("ABCDEF0123456789ABCDEF0123456789", text);
        Assert.Contains("key=***", text);
        Assert.Contains("steamid=7", text);
    }

    [Fact]
    public async Task LogsAFailingStatusWithoutTreatingItAsAnError()
    {
        var provider = new RollingFileLoggerProvider(
            new LogFileOptions(_directory), () => DateTimeOffset.UnixEpoch);
        var log = new LoggerFactory([provider]).CreateLogger<LoggingHandler>();

        using var client = ClientOver(log, () => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await client.GetAsync("https://api.steampowered.com/x");

        provider.Dispose();

        // A bare Contains("401") would still pass if a regression routed
        // non-success statuses through LogError -- "401" appears in the
        // message either way. LogLine.Format separates its fields with
        // exactly two spaces ("...Z  " + level + "  " + category + "  " +
        // message), so the level abbreviation is asserted precisely rather
        // than as a substring that could appear anywhere else in the line.
        var line = FindLine(Path.Combine(_directory, "log.txt"), "401");

        Assert.Contains("  DBG  ", line);
        Assert.DoesNotContain("  ERR  ", line);
    }

    [Fact]
    public async Task LogsACancelledRequestAtDebugAndStillPropagatesIt()
    {
        var provider = new RollingFileLoggerProvider(
            new LogFileOptions(_directory), () => DateTimeOffset.UnixEpoch);
        var log = new LoggerFactory([provider]).CreateLogger<LoggingHandler>();

        using var client = ClientOver(log, () => throw new OperationCanceledException("stopped"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // ThrowsAnyAsync, not ThrowsAsync: HttpClient itself rewraps a
        // cancelled request into a TaskCanceledException before it reaches
        // the caller, regardless of what LoggingHandler rethrows -- verified
        // by observation, not assumed. TaskCanceledException derives from
        // OperationCanceledException, and that is the contract this test
        // cares about: the exception still propagates, whatever its exact
        // runtime type.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync("https://api.steampowered.com/x", cts.Token));

        provider.Dispose();

        // Same shape check as the failing-status test: the cancelled line
        // must carry the DBG marker, not ERR, and the exception must still
        // reach the caller -- a pause is not swallowed, only not misreported.
        var line = FindLine(Path.Combine(_directory, "log.txt"), "https://api.steampowered.com/x");

        Assert.Contains("  DBG  ", line);
        Assert.DoesNotContain("  ERR  ", line);
    }

    [Fact]
    public async Task RethrowsATransportFailureAfterLoggingIt()
    {
        var provider = new RollingFileLoggerProvider(
            new LogFileOptions(_directory), () => DateTimeOffset.UnixEpoch);
        var log = new LoggerFactory([provider]).CreateLogger<LoggingHandler>();

        using var client = ClientOver(log, () => throw new HttpRequestException("no route to host"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("https://api.steampowered.com/x"));

        provider.Dispose();
        var text = File.ReadAllText(Path.Combine(_directory, "log.txt"));

        Assert.Contains("ERR", text);
        Assert.Contains("no route to host", text);
    }

    [Fact]
    public async Task WorksWithANullLogger()
    {
        using var client = ClientOver(
            NullLogger<LoggingHandler>.Instance, () => new HttpResponseMessage(HttpStatusCode.OK));

        var response = await client.GetAsync("https://api.steampowered.com/x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
