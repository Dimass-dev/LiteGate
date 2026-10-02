namespace LiteGate.Gateway.Configuration;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    /// <summary>Paths served by the gateway itself; they bypass proxying, caching and rate limiting.</summary>
    public string[] ReservedPaths { get; set; } = ["/_health", "/_metrics"];

    public UpstreamOptions Upstream { get; set; } = new();

    public CacheOptions Cache { get; set; } = new();

    public RateLimitingOptions RateLimiting { get; set; } = new();

    public ObservabilityOptions Observability { get; set; } = new();
}

public sealed class UpstreamOptions
{
    public string BaseAddress { get; set; } = "http://localhost:5100";

    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class CacheOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Fallback lifetime for cached responses when the upstream sends no Cache-Control.</summary>
    public int DurationSeconds { get; set; } = 30;

    /// <summary>Responses larger than this are streamed through and never cached.</summary>
    public long MaxCacheableBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>Upper bound for the in-memory cache store; entries are sized by body length.</summary>
    public long MaxStoreBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>Additional request headers that partition the cache key.</summary>
    public string[] VaryByHeaders { get; set; } = [];
}

public sealed class RateLimitingOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum burst size; the bucket starts full.</summary>
    public int Capacity { get; set; } = 10;

    /// <summary>Tokens added to every bucket per refill period.</summary>
    public int RefillTokens { get; set; } = 5;

    public double RefillPeriodSeconds { get; set; } = 1;

    /// <summary>Buckets idle longer than this are evicted by the background sweep.</summary>
    public int IdleBucketEvictionMinutes { get; set; } = 10;

    /// <summary>Trust the first X-Forwarded-For hop as the client identity.</summary>
    public bool TrustXForwardedFor { get; set; }
}

public sealed class ObservabilityOptions
{
    /// <summary>Rolling window size for latency percentile calculation.</summary>
    public int LatencySampleWindowSize { get; set; } = 1024;
}
