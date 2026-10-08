namespace LiteGate.Gateway.RateLimiting;

/// <summary>
/// Classic token bucket: starts full, consumes one token per request and refills continuously
/// at <c>refillTokens / refillPeriod</c> tokens per second, capped at the bucket capacity.
/// Thread-safe; virtual time comes from an injected TimeProvider for deterministic tests.
/// </summary>
public sealed class TokenBucket
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly int _capacity;
    private readonly double _refillTokensPerSecond;
    private double _tokens;
    private DateTimeOffset _lastRefillUtc;

    public TokenBucket(int capacity, int refillTokens, TimeSpan refillPeriod, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refillTokens);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refillPeriod.Ticks);

        _capacity = capacity;
        _refillTokensPerSecond = refillTokens / refillPeriod.TotalSeconds;
        _timeProvider = timeProvider;
        _tokens = capacity;
        _lastRefillUtc = timeProvider.GetUtcNow();
    }

    public int Capacity => _capacity;

    public bool TryAcquire(out RateLimitState state)
    {
        lock (_sync)
        {
            var nowUtc = _timeProvider.GetUtcNow();
            var elapsed = nowUtc - _lastRefillUtc;
            if (elapsed > TimeSpan.Zero)
            {
                _tokens = Math.Min(_capacity, _tokens + elapsed.TotalSeconds * _refillTokensPerSecond);
                _lastRefillUtc = nowUtc;
            }

            if (_tokens >= 1d)
            {
                _tokens -= 1d;
                state = new RateLimitState(_capacity, Math.Floor(_tokens), TimeSpan.Zero, TimeUntilFull());
                return true;
            }

            state = new RateLimitState(
                _capacity,
                0d,
                TimeSpan.FromSeconds((1d - _tokens) / _refillTokensPerSecond),
                TimeUntilFull());
            return false;
        }
    }

    private TimeSpan TimeUntilFull() =>
        TimeSpan.FromSeconds(Math.Max(0d, (_capacity - _tokens) / _refillTokensPerSecond));
}

public readonly record struct RateLimitState(
    int Limit,
    double RemainingTokens,
    TimeSpan RetryAfter,
    TimeSpan ResetAfter);
