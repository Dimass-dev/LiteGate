using LiteGate.Gateway.Configuration;
using LiteGate.Gateway.Observability;
using Microsoft.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace LiteGate.Gateway.Caching;

/// <summary>
/// Serves cacheable GET requests from memory (X-Cache: HIT) or captures the upstream response
/// on the way out (X-Cache: MISS). Honors upstream Cache-Control: no-store/no-cache/private and max-age.
/// </summary>
public sealed class ResponseCacheMiddleware(
    RequestDelegate next,
    IResponseCache cache,
    TimeProvider timeProvider,
    GatewayMetrics metrics,
    IOptions<GatewayOptions> options,
    ILogger<ResponseCacheMiddleware> logger)
{
    private static readonly string[] ExcludedFromCacheHeaders =
    [
        "Date", "Age", "X-Cache", "X-Trace-Id", "Content-Length",
        // Transport framing / connection-scoped headers must never be replayed from cache:
        // Kestrel surfaces "Transfer-Encoding: chunked" in the headers collection once a
        // chunked response has started, and copying it back corrupts the HIT response framing.
        "Transfer-Encoding", "Connection", "Keep-Alive", "Upgrade",
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var gatewayOptions = options.Value;
        var request = context.Request;
        var cacheable = gatewayOptions.Cache.Enabled
            && HttpMethods.IsGet(request.Method)
            && context.GetEndpoint() is null
            && !context.IsReservedPath(gatewayOptions);
        if (!cacheable)
        {
            await next(context);
            return;
        }

        var tracker = context.GetStageTracker();
        var cacheKey = CacheKeyBuilder.Build(request, gatewayOptions.Cache);

        CachedResponse? cached;
        using (tracker.BeginStage(StageNames.CacheLookup))
        {
            cache.TryGet(cacheKey, out cached);
        }

        if (cached is not null)
        {
            metrics.IncrementCacheHit();
            tracker.CacheHit = true;
            logger.LogDebug(
                "Cache hit for {Method} {Path} (age {AgeSeconds} s)",
                request.Method,
                request.Path.Value,
                (int)(timeProvider.GetUtcNow() - cached.StoredAtUtc).TotalSeconds);
            await WriteCachedResponseAsync(context, cached, timeProvider);
            return;
        }

        context.Response.Headers["X-Cache"] = "MISS";
        metrics.IncrementCacheMiss();
        var originalBody = context.Response.Body;
        var bufferingStream = new ResponseBufferingStream(originalBody, gatewayOptions.Cache.MaxCacheableBodyBytes);
        context.Response.Body = bufferingStream;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        if (bufferingStream.CacheCandidate
            && context.Response.StatusCode == StatusCodes.Status200OK
            && !context.Response.Headers.ContainsKey("Set-Cookie")
            && TryGetLifetime(context.Response.Headers, gatewayOptions.Cache, out var lifetime))
        {
            var captured = new CachedResponse(
                StatusCodes.Status200OK,
                SnapshotHeaders(context.Response.Headers),
                bufferingStream.GetBufferedBytes(),
                timeProvider.GetUtcNow());
            using (tracker.BeginStage(StageNames.CacheStore))
            {
                cache.Set(cacheKey, captured, lifetime);
            }
            logger.LogDebug(
                "Cached GET response for {Path} for {LifetimeSeconds} s ({Bytes} bytes)",
                request.Path.Value,
                lifetime.TotalSeconds,
                captured.Body.Length);
        }
        else
        {
            logger.LogDebug("Response for {Method} {Path} was not cacheable", request.Method, request.Path.Value);
        }
    }

    private static async Task WriteCachedResponseAsync(HttpContext context, CachedResponse cached, TimeProvider timeProvider)
    {
        context.Response.StatusCode = cached.StatusCode;
        foreach (var (name, values) in cached.Headers)
        {
            context.Response.Headers[name] = values;
        }

        context.Response.ContentLength = cached.Body.Length;
        context.Response.Headers["Age"] = Math.Max(0, (int)(timeProvider.GetUtcNow() - cached.StoredAtUtc).TotalSeconds).ToString();
        context.Response.Headers["X-Cache"] = "HIT";
        await context.Response.Body.WriteAsync(cached.Body, context.RequestAborted);
    }

    private static bool TryGetLifetime(IHeaderDictionary headers, CacheOptions cacheOptions, out TimeSpan lifetime)
    {
        var defaultLifetime = TimeSpan.FromSeconds(cacheOptions.DurationSeconds);
        if (!CacheControlHeaderValue.TryParse(headers.CacheControl.ToString(), out var cacheControl))
        {
            lifetime = defaultLifetime;
            return true;
        }

        if (cacheControl.NoStore == true || cacheControl.NoCache == true || cacheControl.Private == true)
        {
            lifetime = defaultLifetime;
            return false;
        }

        lifetime = cacheControl.MaxAge ?? defaultLifetime;
        return true;
    }

    private static Dictionary<string, string[]> SnapshotHeaders(IHeaderDictionary headers)
    {
        var snapshot = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in headers)
        {
            if (ExcludedFromCacheHeaders.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            snapshot[name] = values.ToArray()!; // StringValues never holds null entries
        }

        return snapshot;
    }
}
