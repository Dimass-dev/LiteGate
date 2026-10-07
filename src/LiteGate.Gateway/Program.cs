using LiteGate.Gateway.Caching;
using LiteGate.Gateway.Configuration;
using LiteGate.Gateway.Observability;
using LiteGate.Gateway.Proxy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache(memoryCacheOptions =>
{
    var gatewayOptions = builder.Configuration.GetSection(GatewayOptions.SectionName).Get<GatewayOptions>() ?? new GatewayOptions();
    memoryCacheOptions.SizeLimit = gatewayOptions.Cache.MaxStoreBytes;
});
builder.Services.AddSingleton<IResponseCache, MemoryResponseCache>();
builder.Services.AddSingleton<HttpMessageHandler>(_ => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(1),
});
builder.Services.AddSingleton<UpstreamForwarder>();

var app = builder.Build();

app.UseRouting();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ResponseCacheMiddleware>();
app.UseMiddleware<ReverseProxyMiddleware>();

app.MapGet("/_health", () => Results.Ok(new { status = "healthy", service = "LiteGate" }));

app.Run();

public partial class Program { }
