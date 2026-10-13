using System.Net;
using System.Text;
using Xunit;

namespace LiteGate.Tests;

public class ProxyForwardingTests
{
    [Fact]
    public async Task Custom_headers_are_forwarded_and_forwarded_headers_are_added()
    {
        using var factory = new GatewayTestFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Custom-Header", "abc123");

        var response = await client.GetAsync("/api/data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("abc123", factory.Stub.LastRequestHeaders["X-Custom-Header"]);
        Assert.True(factory.Stub.LastRequestHeaders.ContainsKey("X-Forwarded-For"));
        Assert.True(factory.Stub.LastRequestHeaders.ContainsKey("X-Forwarded-Host"));
        Assert.True(factory.Stub.LastRequestHeaders.ContainsKey("X-Forwarded-Proto"));
    }

    [Fact]
    public async Task Upstream_status_and_headers_are_proxied_back()
    {
        using var factory = new GatewayTestFactory();
        factory.Stub.Responder = static _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"created":true}""", Encoding.UTF8, "application/json"),
            };
            response.Headers.Add("X-Upstream-Header", "yes");
            return response;
        };
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/data");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("yes", response.Headers.GetValues("X-Upstream-Header").Single());
        Assert.Contains("\"created\":true", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unreachable_upstream_returns_502()
    {
        using var factory = new GatewayTestFactory();
        factory.Stub.Responder = static _ => throw new HttpRequestException("connection refused");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/data");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("upstream_unreachable", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_bodies_are_forwarded_to_the_upstream()
    {
        using var factory = new GatewayTestFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/echo", new StringContent("""{"x":1}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"x\":1", factory.Stub.LastRequestBody);
    }
}
