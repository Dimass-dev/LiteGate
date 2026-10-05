using LiteGate.Gateway.Configuration;
using LiteGate.Gateway.Observability;
using Microsoft.Extensions.Options;

namespace LiteGate.Gateway.Proxy;

/// <summary>
/// Terminal middleware: forwards everything that was not matched by a local endpoint
/// and was not short-circuited by rate limiting or the response cache.
/// </summary>
public sealed class ReverseProxyMiddleware(
    RequestDelegate next,
    UpstreamForwarder forwarder,
    IOptions<GatewayOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var gatewayOptions = options.Value;
        if (context.GetEndpoint() is not null || context.IsReservedPath(gatewayOptions))
        {
            await next(context);
            return;
        }

        var tracker = context.GetStageTracker();
        using (tracker.BeginStage(StageNames.Upstream))
        {
            await forwarder.ForwardAsync(context);
        }
    }
}
