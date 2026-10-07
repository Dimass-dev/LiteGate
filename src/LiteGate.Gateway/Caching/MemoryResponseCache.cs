using Microsoft.Extensions.Caching.Memory;

namespace LiteGate.Gateway.Caching;

/// <summary>IMemoryCache-backed implementation; entries are sized by body length.</summary>
public sealed class MemoryResponseCache(IMemoryCache cache) : IResponseCache
{
    public bool TryGet(string key, out CachedResponse? response)
    {
        if (cache.TryGetValue(key, out CachedResponse? cached))
        {
            response = cached;
            return true;
        }

        response = null;
        return false;
    }

    public void Set(string key, CachedResponse response, TimeSpan lifetime) =>
        cache.Set(key, response, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = lifetime,
            Size = response.Body.Length + 256,
        });
}
