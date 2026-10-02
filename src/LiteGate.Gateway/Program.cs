using LiteGate.Gateway.Configuration;
using LiteGate.Gateway.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.UseRouting();
app.UseMiddleware<RequestLoggingMiddleware>();

app.MapGet("/_health", () => Results.Ok(new { status = "healthy", service = "LiteGate" }));

app.Run();

public partial class Program { }
