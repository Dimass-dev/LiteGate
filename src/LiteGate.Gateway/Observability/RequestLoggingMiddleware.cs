namespace LiteGate.Gateway.Observability;

/// <summary>
/// Outermost middleware: assigns a trace id, starts the stage tracker and emits
/// one structured log entry per request with per-stage latency and the final status code.
/// </summary>
public sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILoggerFactory loggerFactory,
    ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = string.IsNullOrEmpty(context.TraceIdentifier)
            ? Guid.NewGuid().ToString("N")
            : context.TraceIdentifier;
        context.Items[StageTracker.HttpContextItemsKey] = new StageTracker(loggerFactory.CreateLogger<StageTracker>());
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Trace-Id"] = traceId;
            return Task.CompletedTask;
        });

        try
        {
            await next(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Gateway request {TraceId} {Method} {Path} failed",
                traceId,
                context.Request.Method,
                context.Request.Path.Value);
            throw;
        }
        finally
        {
            var tracker = context.GetStageTracker();
            logger.LogInformation(
                "Gateway request {TraceId} {Method} {Path} responded {StatusCode} in {TotalMs} ms (cacheHit={CacheHit}, throttled={Throttled}) | stages: rateLimit={RateLimitMs} ms, cacheLookup={CacheLookupMs} ms, cacheStore={CacheStoreMs} ms, upstream={UpstreamMs} ms",
                traceId,
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                tracker.TotalMs,
                tracker.CacheHit,
                tracker.Throttled,
                tracker.GetStageMs(StageNames.RateLimit),
                tracker.GetStageMs(StageNames.CacheLookup),
                tracker.GetStageMs(StageNames.CacheStore),
                tracker.GetStageMs(StageNames.Upstream));
        }
    }
}
