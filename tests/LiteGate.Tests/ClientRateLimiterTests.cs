using LiteGate.Gateway.Configuration;
using LiteGate.Gateway.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LiteGate.Tests;

public class ClientRateLimiterTests
{
    private static ClientRateLimiter CreateLimiter(FakeTimeProvider timeProvider)
    {
        var rateLimiting = new RateLimitingOptions
        {
            Capacity = 3,
            RefillTokens = 1,
            RefillPeriodSeconds = 60,
            IdleBucketEvictionMinutes = 10,
        };
        return new ClientRateLimiter(
            Options.Create(new GatewayOptions { RateLimiting = rateLimiting }),
            timeProvider,
            NullLogger<ClientRateLimiter>.Instance);
    }

    [Fact]
    public void Buckets_are_isolated_per_client()
    {
        var limiter = CreateLimiter(new FakeTimeProvider());

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.True(limiter.Acquire("203.0.113.10").Allowed);
        }
        Assert.False(limiter.Acquire("203.0.113.10").Allowed);

        Assert.True(limiter.Acquire("203.0.113.11").Allowed);
        Assert.Equal(2, limiter.BucketCount);
    }

    [Fact]
    public void Decision_exposes_limit_and_reset_information()
    {
        var limiter = CreateLimiter(new FakeTimeProvider());

        var decision = limiter.Acquire("198.51.100.7");

        Assert.True(decision.Allowed);
        Assert.Equal(3, decision.State.Limit);
        Assert.Equal(2.0, decision.State.RemainingTokens, 3);
        Assert.Equal(TimeSpan.Zero, decision.State.RetryAfter);
        Assert.Equal(60.0, decision.State.ResetAfter.TotalSeconds, 3); // one token per 60 s until full again
    }

    [Fact]
    public void Idle_buckets_are_evicted_by_the_sweep()
    {
        var timeProvider = new FakeTimeProvider();
        var limiter = CreateLimiter(timeProvider);

        limiter.Acquire("192.0.2.5");
        Assert.Equal(1, limiter.BucketCount);

        timeProvider.Advance(TimeSpan.FromMinutes(11));
        limiter.SweepIdleBuckets();

        Assert.Equal(0, limiter.BucketCount);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.True(limiter.Acquire("192.0.2.5").Allowed); // fresh full bucket
        }
        Assert.False(limiter.Acquire("192.0.2.5").Allowed);
    }
}
