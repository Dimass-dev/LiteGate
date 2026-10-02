using LiteGate.Gateway.Configuration;

namespace LiteGate.Gateway.Observability;

public static class GatewayHttpContextExtensions
{
    public static StageTracker GetStageTracker(this HttpContext context) =>
        (StageTracker)context.Items[StageTracker.HttpContextItemsKey]!;

    public static bool IsReservedPath(this HttpContext context, GatewayOptions options) =>
        options.ReservedPaths.Contains(context.Request.Path.Value ?? "/", StringComparer.OrdinalIgnoreCase);
}
