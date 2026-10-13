using LiteGate.Gateway.RateLimiting;
using Xunit;

namespace LiteGate.Tests;

public class TokenBucketTests
{
    private static TokenBucket CreateBucket(FakeTimeProvider timeProvider, int capacity = 5, int refillTokens = 2, double refillPeriodSeconds = 1) =>
        new(capacity, refillTokens, TimeSpan.FromSeconds(refillPeriodSeconds), timeProvider);

    [Fact]
    public void Bucket_starts_full_and_allows_burst_up_to_capacity()
    {
        var bucket = CreateBucket(new FakeTimeProvider());

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.True(bucket.TryAcquire(out var state), $"attempt {attempt} should pass");
            Assert.Equal(5, state.Limit);
        }
    }

    [Fact]
    public void Request_over_capacity_is_rejected_with_retry_after()
    {
        var bucket = CreateBucket(new FakeTimeProvider());
        for (var attempt = 0; attempt < 5; attempt++)
        {
            bucket.TryAcquire(out _);
        }

        Assert.False(bucket.TryAcquire(out var state));
        Assert.Equal(0.0, state.RemainingTokens, 3);
        Assert.Equal(0.5, state.RetryAfter.TotalSeconds, 3); // one missing token at 2 tokens/s
    }

    [Fact]
    public void Tokens_refill_continuously_over_time()
    {
        var timeProvider = new FakeTimeProvider();
        var bucket = CreateBucket(timeProvider);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            bucket.TryAcquire(out _);
        }

        timeProvider.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True(bucket.TryAcquire(out _)); // exactly one token refilled
        Assert.False(bucket.TryAcquire(out _));

        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        Assert.False(bucket.TryAcquire(out _)); // only half a token available

        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        Assert.True(bucket.TryAcquire(out _));
    }

    [Fact]
    public void Refill_is_capped_at_capacity()
    {
        var timeProvider = new FakeTimeProvider();
        var bucket = CreateBucket(timeProvider);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            bucket.TryAcquire(out _);
        }

        timeProvider.Advance(TimeSpan.FromSeconds(10));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.True(bucket.TryAcquire(out _), $"attempt {attempt} should pass after refill");
        }
        Assert.False(bucket.TryAcquire(out _));
    }
}
