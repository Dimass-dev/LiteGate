namespace LiteGate.Gateway.Caching;

/// <summary>A captured GET response ready to be replayed from cache.</summary>
public sealed record CachedResponse(
    int StatusCode,
    IReadOnlyDictionary<string, string[]> Headers,
    byte[] Body,
    DateTimeOffset StoredAtUtc);
