using LiteGate.Gateway.Configuration;
using LiteGate.Gateway.Observability;
using Microsoft.Extensions.Options;

namespace LiteGate.Gateway.RateLimiting;

/// <summary>
/// Applies a token bucket per client IP and short-circuits exhausted requests with 429 + Retry-After.
/// </summary>
public sealed class RateLimitingMiddleware(
    RequestDelegate next,
    ClientRateLimiter rateLimiter,
    ClientIpResolver clientIpResolver,
    IOptions<GatewayOptions> options,
    ILogger<RateLimitingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var gatewayOptions = options.Value;
        if (!gatewayOptions.RateLimiting.Enabled ||
            context.GetEndpoint() is not null ||
            context.IsReservedPath(gatewayOptions))
        {
            await next(context);
            return;
        }

        var clientIp = clientIpResolver.Resolve(context);
        var tracker = context.GetStageTracker();
        RateLimitDecision decision;
        using (tracker.BeginStage(StageNames.RateLimit))
        {
            decision = rateLimiter.Acquire(clientIp);
        }

        context.Response.Headers["X-RateLimit-Limit"] = decision.State.Limit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = decision.State.RemainingTokens.ToString("0");
        context.Response.Headers["X-RateLimit-Reset"] = Math.Ceiling(decision.State.ResetAfter.TotalSeconds).ToString("0");

        if (decision.Allowed)
        {
            await next(context);
            return;
        }

        tracker.Throttled = true;
        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(decision.State.RetryAfter.TotalSeconds));
        logger.LogWarning(
            "Rate limit exceeded for {ClientIp} on {Method} {Path}; retry after {RetryAfterSeconds} s",
            clientIp,
            context.Request.Method,
            context.Request.Path.Value,
            retryAfterSeconds);

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
        await context.Response.WriteAsJsonAsync(
            new { error = "rate_limit_exceeded", retryAfterSeconds },
            context.RequestAborted);
    }
}
