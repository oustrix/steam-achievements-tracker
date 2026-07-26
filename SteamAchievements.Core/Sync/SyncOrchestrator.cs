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

    // GameRepository (Data/GameRepository.cs) wraps a single SqliteConnection
    // and is not documented as thread-safe. Microsoft.Data.Sqlite connections
    // are not safe to use concurrently from multiple threads, and ADO.NET only
    // permits one active transaction per connection — several repository
    // methods (UpsertSchema, UpsertGlobalPercentages, ...) open their own
    // transaction internally. With WorkerCount workers calling the repository
    // in parallel, two workers racing to BeginTransaction on the same
    // connection throws "SQLite Error 1: 'cannot start a transaction within a
    // transaction'" (or worse, interleaves writes silently). All repository
    // access from the worker pool is therefore serialized through this lock;
    // only the network calls — which are already rate-limited to ~5 req/s —
    // run in parallel across workers.
    private readonly Lock _dbLock = new();

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

        IReadOnlyDictionary<uint, GameSyncState> states;

        lock (_dbLock)
        {
            _repository.UpsertOwnedGames(owned);
            states = _repository.GetSyncStates();
        }

        var plan = SyncPlanner.Plan(owned, states, DateTimeOffset.UtcNow, _options, force);

        var names = owned.ToDictionary(g => g.AppId, g => g.Name);
        var completed = 0;

        await Parallel.ForEachAsync(
            plan,
            new ParallelOptions { MaxDegreeOfParallelism = WorkerCount, CancellationToken = cancellationToken },
            async (item, token) =>
            {
                await SyncGameAsync(steamId, item, token);

                var done = Interlocked.Increment(ref completed);
                progress?.Report(new SyncProgress(done, plan.Count, names.GetValueOrDefault(item.AppId, string.Empty)));
            });

        // Trend charts come later, but the history behind them cannot be
        // reconstructed after the fact — record it from the very first sync.
        lock (_dbLock)
        {
            _repository.WriteSnapshot(DateTimeOffset.UtcNow);
        }
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
                    lock (_dbLock)
                    {
                        _repository.MarkNoAchievements(item.AppId);
                    }

                    return;
                }

                lock (_dbLock)
                {
                    _repository.UpsertSchema(item.AppId, schema, now);
                }
            }

            if (item.NeedGlobal)
            {
                await _rateLimiter.WaitAsync(cancellationToken);
                var percentages = await _retry.ExecuteAsync(
                    async token => await _client.GetGlobalPercentagesAsync(item.AppId, token), cancellationToken);

                lock (_dbLock)
                {
                    _repository.UpsertGlobalPercentages(item.AppId, percentages);
                }
            }

            if (item.NeedPlayer)
            {
                await _rateLimiter.WaitAsync(cancellationToken);
                var playerAchievements = await _retry.ExecuteAsync(
                    async token => await _client.GetPlayerAchievementsAsync(steamId, item.AppId, token), cancellationToken);

                lock (_dbLock)
                {
                    _repository.UpsertPlayerAchievements(item.AppId, playerAchievements);
                }
            }

            // Written per game, which is what makes an interrupted sync resumable.
            lock (_dbLock)
            {
                _repository.MarkSynced(item.AppId, item.Playtime, now);
            }
        }
        catch (SteamApiException e) when (e.Kind == SteamApiErrorKind.NoStatsForApp)
        {
            // Expected for 30-40% of a library: soundtracks, demos, tools.
            lock (_dbLock)
            {
                _repository.MarkNoAchievements(item.AppId);
            }
        }
        catch (SteamApiException e) when (e.Kind != SteamApiErrorKind.InvalidKey)
        {
            // One bad game must not abort the whole sync; an invalid key must.
            lock (_dbLock)
            {
                _repository.MarkError(item.AppId, e.Message);
            }
        }
    }
}
