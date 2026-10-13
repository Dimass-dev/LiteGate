using LiteGate.Gateway.Caching;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace LiteGate.Tests;

public class MemoryResponseCacheTests
{
    private static CachedResponse CreateResponse() => new(
        200,
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = ["application/json"],
        },
        [1, 2, 3],
        DateTimeOffset.UtcNow);

    [Fact]
    public void Entry_can_be_read_back_before_it_expires()
    {
        var cache = new MemoryResponseCache(new MemoryCache(new MemoryCacheOptions()));

        cache.Set("get|/api/data", CreateResponse(), TimeSpan.FromSeconds(30));

        Assert.True(cache.TryGet("get|/api/data", out var cached));
        Assert.Equal(200, cached!.StatusCode);
        Assert.Equal(3, cached.Body.Length);
    }

    [Fact]
    public async Task Entry_expires_after_its_lifetime()
    {
        var cache = new MemoryResponseCache(new MemoryCache(new MemoryCacheOptions()));

        cache.Set("get|/api/data", CreateResponse(), TimeSpan.FromMilliseconds(80));
        await Task.Delay(200);

        Assert.False(cache.TryGet("get|/api/data", out _));
    }

    [Fact]
    public void Unknown_keys_miss()
    {
        var cache = new MemoryResponseCache(new MemoryCache(new MemoryCacheOptions()));

        Assert.False(cache.TryGet("get|/missing", out _));
    }
}
