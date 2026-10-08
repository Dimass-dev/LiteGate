using System.Collections.Concurrent;
using LiteGate.Gateway.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LiteGate.Gateway.RateLimiting;

/// <summary>
/// Keeps one token bucket per client key and periodically evicts buckets that have been idle
/// for longer than <see cref="RateLimitingOptions.IdleBucketEvictionMinutes"/>.
/// </summary>
public sealed class ClientRateLimiter(
    IOptions<GatewayOptions> options,
    TimeProvider timeProvider,
    ILogger<ClientRateLimiter> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();
    private readonly RateLimitingOptions _rateLimiting = options.Value.RateLimiting;

    public int BucketCount => _buckets.Count;

    public RateLimitDecision Acquire(string clientKey)
    {
        var bucket = _buckets.GetOrAdd(clientKey, static (_, self) => self.CreateBucket(), this);
        var allowed = bucket.Limiter.TryAcquire(out var state);
        bucket.LastSeenUtc = timeProvider.GetUtcNow();

        logger.LogDebug(
            "Rate limit check for {ClientKey}: allowed={Allowed}, remaining={RemainingTokens}",
            clientKey,
            allowed,
            state.RemainingTokens);
        return new RateLimitDecision(allowed, state);
    }

    public void SweepIdleBuckets()
    {
        var cutoffUtc = timeProvider.GetUtcNow() - TimeSpan.FromMinutes(_rateLimiting.IdleBucketEvictionMinutes);
        foreach (var (clientKey, bucket) in _buckets)
        {
            if (bucket.LastSeenUtc < cutoffUtc && _buckets.TryRemove(clientKey, out _))
            {
                logger.LogDebug("Evicted idle rate limit bucket for {ClientKey}", clientKey);
            }
        }
    }

    private Bucket CreateBucket() => new(
        new TokenBucket(
            _rateLimiting.Capacity,
            _rateLimiting.RefillTokens,
            TimeSpan.FromSeconds(_rateLimiting.RefillPeriodSeconds),
            timeProvider))
    {
        LastSeenUtc = timeProvider.GetUtcNow(),
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                SweepIdleBuckets();
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private sealed class Bucket(TokenBucket limiter)
    {
        public TokenBucket Limiter { get; } = limiter;

        public DateTimeOffset LastSeenUtc { get; set; }
    }
}

public readonly record struct RateLimitDecision(bool Allowed, RateLimitState State);
