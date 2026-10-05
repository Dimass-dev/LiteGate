var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();
var requestCount = 0;

app.MapGet("/api/time", () => Results.Ok(new
{
    message = "hello from upstream",
    requestNumber = Interlocked.Increment(ref requestCount),
    servedAtUtc = DateTimeOffset.UtcNow,
}));

app.MapGet("/api/no-store", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new
    {
        message = "this response is never cached",
        requestNumber = Interlocked.Increment(ref requestCount),
    });
});

app.MapGet("/api/slow", async (CancellationToken cancellationToken) =>
{
    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    return Results.Ok(new { message = "slow response" });
});

app.MapPost("/api/echo", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();
    return Results.Ok(new { echoed = body });
});

app.Run();

public partial class Program { }
