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
            // Cannot tell a deliberate pause from a genuine HttpClient timeout
            // here, and that is not an oversight -- it is what this position
            // in the pipeline can see. HttpClient.SendAsyncCore never hands
            // the handler chain the caller's own token: it links it with an
            // internal CancellationTokenSource that HttpClient.Timeout also
            // cancels, and passes that linked token's own Token down through
            // base.SendAsync. So SyncCoordinator.Pause() cancelling the run's
            // token and Steam simply taking longer than HttpClient.Timeout
            // both surface at this catch as the identical exception, against
            // the identical (already-cancelled) token -- there is no local
            // signal left here to tell them apart.
            //
            // Consequently both are logged at Debug, and a real timeout is
            // undercounted at this level: it never gets its own ERR line.
            // That is not lost information, only relocated. The layer that
            // still holds the caller's real, unlinked token is one frame up,
            // in SteamApiClient.GetJsonAsync, which calls _http.GetAsync with
            // that token *before* HttpClient does its own linking. Its
            // "catch (TaskCanceledException)" branch is where a timeout
            // becomes SteamApiException(SteamApiErrorKind.ServerError, ...)
            // -- transient, and retried. That branch is reached only when the
            // preceding "catch (OperationCanceledException) when
            // (cancellationToken.IsCancellationRequested)" in the same method
            // evaluates false, which is the opposite of this one: two
            // visually identical guards, a few frames apart, one meaningful
            // and one not. GetJsonAsync's guard answers a real question,
            // because the token it inspects is still the caller's own,
            // untouched by HttpClient. This guard cannot, because by the time
            // control reaches here HttpClient has already replaced that
            // token with the linked one described above. This handler only
            // records that a request ended, not why; deciding what the end
            // meant is SteamApiClient's job, not this one's.
            //
            // The guard is kept anyway, even though it can never evaluate
            // false at this position, because dropping it would make this
            // catch read as deliberately swallowing every
            // OperationCanceledException regardless of cause -- which is not
            // what is happening, even if the two causes end up indistinguishable
            // here.
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
