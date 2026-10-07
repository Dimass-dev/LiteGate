namespace LiteGate.Gateway.Caching;

/// <summary>Storage abstraction for cached upstream responses (in-memory today, Redis later).</summary>
public interface IResponseCache
{
    bool TryGet(string key, out CachedResponse? response);

    void Set(string key, CachedResponse response, TimeSpan lifetime);
}
