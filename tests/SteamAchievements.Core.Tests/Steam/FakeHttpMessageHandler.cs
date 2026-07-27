using System.Net;

namespace SteamAchievements.Core.Tests.Steam;

public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    // Backed by a thread-safe collection: SyncOrchestrator drives this handler
    // from a worker pool, so SendAsync can be entered concurrently from
    // multiple threads. A plain List<Uri> is not safe under concurrent Add.
    private readonly System.Collections.Concurrent.ConcurrentQueue<Uri> _requests = new();

    public IReadOnlyCollection<Uri> Requests => _requests;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    public static FakeHttpMessageHandler Returning(HttpStatusCode status, string body, string contentType = "application/json") =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, contentType),
        });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _requests.Enqueue(request.RequestUri!);
        return Task.FromResult(_responder(request));
    }
}
