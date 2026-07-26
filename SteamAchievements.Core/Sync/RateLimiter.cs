namespace SteamAchievements.Core.Sync;

/// <summary>
/// Simple token bucket. Steam tolerates roughly five requests per second
/// before answering 429.
/// </summary>
public sealed class RateLimiter
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _interval;
    private DateTimeOffset _nextSlot = DateTimeOffset.MinValue;

    public RateLimiter(double requestsPerSecond) =>
        _interval = TimeSpan.FromSeconds(1 / requestsPerSecond);

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var now = DateTimeOffset.UtcNow;
            var wait = _nextSlot - now;

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
                now = DateTimeOffset.UtcNow;
            }

            _nextSlot = now + _interval;
        }
        finally
        {
            _gate.Release();
        }
    }
}
