using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// Records every Steam request: method, URL, status and how long it took.
///
/// A <see cref="DelegatingHandler"/> rather than a change to
/// <c>SteamApiClient</c>. The client has no reason to know its traffic is
/// observed, the CLI already composes handlers this way, and a stub inner
/// handler makes this testable without a network.
///
/// The URL carries the API key in its query string. Nothing is stripped here
/// on purpose — <see cref="Redaction"/> runs inside the writer, so a URL
/// logged from anywhere else is covered by the same rule rather than by a
/// second copy of it that can drift.
/// </summary>
public sealed class LoggingHandler : DelegatingHandler
{
    private readonly ILogger<LoggingHandler> _log;

    public LoggingHandler(ILogger<LoggingHandler> log) => _log = log;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var url = request.RequestUri?.ToString() ?? "(no uri)";

        try
        {
            var response = await base.SendAsync(request, cancellationToken);

            // A 401 or a 429 is data, not a failure of this handler: the error
            // taxonomy in SteamApiClient decides what it means.
            _log.LogDebug(
                "{Method} {Url} -> {Status} in {Elapsed}ms",
                request.Method.Method, url, (int)response.StatusCode, Elapsed(started));

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // SyncCoordinator.Pause cancels the run's token, and
            // SyncOrchestrator drives several requests concurrently, so one
            // deliberate pause would otherwise write a burst of ERR lines --
            // indistinguishable from several real transport failures in the
            // one artifact meant to make the first real failure obvious. A
            // cancelled request is neither a status Steam returned nor a
            // transport failure, so it gets its own line at the same level
            // as success.
            _log.LogDebug(
                "{Method} {Url} -> cancelled after {Elapsed}ms", request.Method.Method, url, Elapsed(started));
            throw;
        }
        catch (Exception e)
        {
            _log.LogError(
                e, "{Method} {Url} -> failed after {Elapsed}ms", request.Method.Method, url, Elapsed(started));
            throw;
        }
    }

    private static long Elapsed(long started) =>
        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
