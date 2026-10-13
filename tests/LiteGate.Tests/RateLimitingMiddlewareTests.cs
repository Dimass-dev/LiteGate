using System.Net;
using Xunit;

namespace LiteGate.Tests;

public class RateLimitingMiddlewareTests
{
    private static GatewayTestFactory CreateFactory() => new(new Dictionary<string, string?>
    {
        ["Gateway:RateLimiting:Capacity"] = "3",
        ["Gateway:RateLimiting:RefillTokens"] = "1",
        ["Gateway:RateLimiting:RefillPeriodSeconds"] = "60",
    });

    [Fact]
    public async Task Requests_over_capacity_are_throttled_with_429()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var allowed = await client.GetAsync("/api/data");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var throttled = await client.GetAsync("/api/data");

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.True(throttled.Headers.Contains("Retry-After"));
        Assert.Equal("3", throttled.Headers.GetValues("X-RateLimit-Limit").Single());
        Assert.Equal("0", throttled.Headers.GetValues("X-RateLimit-Remaining").Single());
        Assert.Contains("rate_limit_exceeded", await throttled.Content.ReadAsStringAsync());
        // Every request consumes a token (rate limiting runs before the cache),
        // but only the first GET reaches the upstream: the rest are cache hits.
        Assert.Equal(1, factory.Stub.CallCount);
    }

    [Fact]
    public async Task Reserved_paths_are_not_rate_limited()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/_health")).StatusCode);
        }
    }
}
