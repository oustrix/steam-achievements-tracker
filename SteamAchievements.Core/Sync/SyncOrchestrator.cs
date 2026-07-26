using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using SteamAchievements.Core.Data;
using SteamAchievements.Core.Steam;

namespace SteamAchievements.Core.Sync;

public sealed class SyncOrchestrator
{
    private const int WorkerCount = 4;

    private readonly SteamApiClient _client;
    private readonly GameRepository _repository;
    private readonly SyncOptions _options;
    private readonly RateLimiter _rateLimiter = new(requestsPerSecond: 5);
    private readonly ResiliencePipeline _retry;

    /// <summary>
    /// The production retry base delay: 1s, 2s, 4s, 8s under exponential
    /// backoff. Tests inject a much smaller value via <paramref name="retryBaseDelay"/>
    /// so a retry-exercising test runs in milliseconds instead of real seconds
    /// — the backoff shape (exponential, 4 attempts) stays identical either way.
    /// </summary>
    public SyncOrchestrator(
        SteamApiClient client, GameRepository repository, SyncOptions options, TimeSpan? retryBaseDelay = null)
    {
        _client = client;
        _repository = repository;
        _options = options;

        _retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                // Only transient failures are worth retrying. An invalid key
                // is permanent and must surface immediately.
                ShouldHandle = new PredicateBuilder()
                    .Handle<SteamApiException>(e => e.IsTransient),
                MaxRetryAttempts = 4,
                BackoffType = DelayBackoffType.Exponential,
                Delay = retryBaseDelay ?? TimeSpan.FromSeconds(1),
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                // Polly v8 breaks on a failure ratio within a sampling window
                // rather than on N consecutive failures, so this approximates
                // the spec's "five in a row": every call failing across a
                // 30-second window with at least five calls trips the breaker
                // and stops hammering Steam.
                ShouldHandle = new PredicateBuilder()
                    .Handle<SteamApiException>(e => e.IsTransient),
                FailureRatio = 1.0,
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30),
            })
            .Build();
    }

    public async Task RunAsync(
        ulong steamId,
        bool force,
        IProgress<SyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var owned = await _retry.ExecuteAsync(
            async token => await _client.GetOwnedGamesAsync(steamId, token), cancellationToken);

        _repository.UpsertOwnedGames(owned);
        var states = _repository.GetSyncStates();

        var plan = SyncPlanner.Plan(owned, states, DateTimeOffset.UtcNow, _options, force);

        var names = owned.ToDictionary(g => g.AppId, g => g.Name);
        var completed = 0;

        try
        {
            await Parallel.ForEachAsync(
                plan,
                new ParallelOptions { MaxDegreeOfParallelism = WorkerCount, CancellationToken = cancellationToken },
                async (item, token) =>
                {
                    await SyncGameAsync(steamId, item, token);

                    var done = Interlocked.Increment(ref completed);
                    progress?.Report(new SyncProgress(done, plan.Count, names.GetValueOrDefault(item.AppId, string.Empty)));
                });
        }
        catch (AggregateException aggregate)
        {
            // With WorkerCount workers sharing one key, an invalid key gets
            // rejected by several workers before cancellation stops the
            // rest, so Parallel.ForEachAsync wraps them in an
            // AggregateException instead of surfacing the bare
            // SteamApiException a caller can catch on Kind == InvalidKey.
            ThrowUnwrapped(aggregate, cancellationToken);
            throw; // unreachable: ThrowUnwrapped always throws.
        }

        // Trend charts come later, but the history behind them cannot be
        // reconstructed after the fact — record it from the very first sync.
        _repository.WriteSnapshot(DateTimeOffset.UtcNow);
    }

    private async Task SyncGameAsync(ulong steamId, SyncWorkItem item, CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;

            if (item.NeedSchema)
            {
                await _rateLimiter.WaitAsync(cancellationToken);
                var schema = await _retry.ExecuteAsync(
                    async token => await _client.GetSchemaForGameAsync(item.AppId, token), cancellationToken);

                if (schema.Count == 0)
                {
                    _repository.MarkNoAchievements(item.AppId);
                    return;
                }

                _repository.UpsertSchema(item.AppId, schema, now);
            }

            if (item.NeedGlobal)
            {
                await _rateLimiter.WaitAsync(cancellationToken);
                var percentages = await _retry.ExecuteAsync(
                    async token => await _client.GetGlobalPercentagesAsync(item.AppId, token), cancellationToken);

                _repository.UpsertGlobalPercentages(item.AppId, percentages);
            }

            if (item.NeedPlayer)
            {
                await _rateLimiter.WaitAsync(cancellationToken);
                var playerAchievements = await _retry.ExecuteAsync(
                    async token => await _client.GetPlayerAchievementsAsync(steamId, item.AppId, token), cancellationToken);

                _repository.UpsertPlayerAchievements(item.AppId, playerAchievements);
            }

            // Written per game, which is what makes an interrupted sync resumable.
            _repository.MarkSynced(item.AppId, item.Playtime, now);
        }
        catch (SteamApiException e) when (e.Kind == SteamApiErrorKind.NoStatsForApp)
        {
            // Expected for 30-40% of a library: soundtracks, demos, tools.
            _repository.MarkNoAchievements(item.AppId);
        }
        catch (SteamApiException e) when (e.Kind != SteamApiErrorKind.InvalidKey)
        {
            // One bad game must not abort the whole sync; an invalid key must.
            _repository.MarkError(item.AppId, e.Message);
        }
    }

    /// <summary>
    /// Reduces an <see cref="AggregateException"/> from <see cref="Parallel.ForEachAsync{TSource}(IEnumerable{TSource}, ParallelOptions, Func{TSource, CancellationToken, ValueTask})"/>
    /// back down to a single exception, so a caller can write
    /// <c>catch (SteamApiException e) when (e.Kind == SteamApiErrorKind.InvalidKey)</c>
    /// around <see cref="RunAsync"/> without worrying how many workers were
    /// in flight when the failure happened.
    /// </summary>
    [DoesNotReturn]
    private static void ThrowUnwrapped(AggregateException aggregate, CancellationToken cancellationToken)
    {
        var inner = aggregate.Flatten().InnerExceptions;

        // Genuine caller cancellation must still surface as
        // OperationCanceledException, even if it raced with other workers'
        // failures and got bundled into the same aggregate.
        if (cancellationToken.IsCancellationRequested)
        {
            var cancellation = inner.OfType<OperationCanceledException>().FirstOrDefault();
            if (cancellation is not null)
            {
                ExceptionDispatchInfo.Capture(cancellation).Throw();
            }
        }

        if (inner.Count > 0 && inner.All(e => e is SteamApiException))
        {
            var steamErrors = inner.Cast<SteamApiException>().ToList();

            // Several workers can independently discover the same invalid
            // key; any one of them carries the same actionable information,
            // so surface that rather than an arbitrary other kind.
            var mostSignificant = steamErrors.FirstOrDefault(e => e.Kind == SteamApiErrorKind.InvalidKey)
                ?? steamErrors[0];
            ExceptionDispatchInfo.Capture(mostSignificant).Throw();
        }

        // Genuinely different concurrent failures (e.g. not all the same
        // exception kind) — don't discard information by arbitrarily
        // picking one; surface the aggregate as-is.
        ExceptionDispatchInfo.Capture(aggregate).Throw();
    }
}
