var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/_health", () => Results.Ok(new { status = "healthy", service = "LiteGate" }));

app.Run();

public partial class Program { }
