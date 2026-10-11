using LiteGate.Gateway.Caching;
using LiteGate.Gateway.Configuration;
using LiteGate.Gateway.Observability;
using LiteGate.Gateway.Proxy;
using LiteGate.Gateway.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache(memoryCacheOptions =>
{
    var gatewayOptions = builder.Configuration.GetSection(GatewayOptions.SectionName).Get<GatewayOptions>() ?? new GatewayOptions();
    memoryCacheOptions.SizeLimit = gatewayOptions.Cache.MaxStoreBytes;
});
builder.Services.AddSingleton<IResponseCache, MemoryResponseCache>();
builder.Services.AddSingleton<GatewayMetrics>();
builder.Services.AddSingleton<ClientRateLimiter>();
builder.Services.AddHostedService(static serviceProvider => serviceProvider.GetRequiredService<ClientRateLimiter>());
builder.Services.AddSingleton<ClientIpResolver>();
builder.Services.AddSingleton<HttpMessageHandler>(_ => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(1),
});
builder.Services.AddSingleton<UpstreamForwarder>();

var app = builder.Build();

app.UseRouting();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();
app.UseMiddleware<ResponseCacheMiddleware>();
app.UseMiddleware<ReverseProxyMiddleware>();

app.MapGet("/_health", () => Results.Ok(new { status = "healthy", service = "LiteGate" }));
app.MapGet("/_metrics", (GatewayMetrics metrics) => Results.Json(metrics.Snapshot()));

app.Run();

public partial class Program { }
