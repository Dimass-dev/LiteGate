using LiteGate.Gateway.Configuration;
using Microsoft.Extensions.Options;

namespace LiteGate.Gateway.RateLimiting;

/// <summary>Resolves the client identity used for rate limiting.</summary>
public sealed class ClientIpResolver(IOptions<GatewayOptions> options)
{
    public string Resolve(HttpContext context)
    {
        var rateLimiting = options.Value.RateLimiting;
        if (rateLimiting.TrustXForwardedFor &&
            context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            var firstHop = forwarded.ToString().Split(',')[0].Trim();
            if (firstHop.Length > 0)
            {
                return firstHop;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
