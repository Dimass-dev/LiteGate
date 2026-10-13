using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LiteGate.Tests;

/// <summary>
/// Boots the gateway in-memory with the real upstream HttpClient replaced by a stub handler,
/// so the whole middleware pipeline runs without any network.
/// </summary>
public sealed class GatewayTestFactory(
    Dictionary<string, string?>? configurationOverrides = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    public StubUpstreamHandler Stub { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configurationBuilder =>
        {
            if (configurationOverrides is not null)
            {
                configurationBuilder.AddInMemoryCollection(configurationOverrides);
            }
        });
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<HttpMessageHandler>(Stub);
            configureServices?.Invoke(services);
        });
    }
}
