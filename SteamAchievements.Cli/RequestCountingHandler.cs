namespace SteamAchievements.Cli;

/// <summary>
/// Counts every HTTP request issued through the wrapped handler. Cheap to
/// obtain because it rides on the handler chain every request already goes
/// through, rather than requiring SteamApiClient to expose a counter itself.
/// </summary>
public sealed class RequestCountingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private int _count;

    public int Count => _count;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _count);
        return await base.SendAsync(request, cancellationToken);
    }
}
