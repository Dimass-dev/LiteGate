using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace LiteGate.Tests;

public class CacheMiddlewareTests
{
    [Fact]
    public async Task Identical_get_requests_are_served_from_cache()
    {
        using var factory = new GatewayTestFactory();
        var client = factory.CreateClient();

        var first = await client.GetAsync("/api/data");
        var second = await client.GetAsync("/api/data");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("MISS", first.Headers.GetValues("X-Cache").Single());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("HIT", second.Headers.GetValues("X-Cache").Single());
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(1, factory.Stub.CallCount);
    }

    [Fact]
    public async Task Distinct_query_strings_are_cached_separately()
    {
        using var factory = new GatewayTestFactory();
        var client = factory.CreateClient();

        await client.GetAsync("/api/data?page=1");
        await client.GetAsync("/api/data?page=2");

        Assert.Equal(2, factory.Stub.CallCount);
    }

    [Fact]
    public async Task Non_get_requests_bypass_the_cache()
    {
        using var factory = new GatewayTestFactory();
        var client = factory.CreateClient();

        var first = await client.PostAsync("/api/echo", new StringContent("""{"x":1}""", Encoding.UTF8, "application/json"));
        var second = await client.PostAsync("/api/echo", new StringContent("""{"x":1}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False(second.Headers.Contains("X-Cache"));
        Assert.Equal(2, factory.Stub.CallCount);
        Assert.Contains("\"x\":1", factory.Stub.LastRequestBody);
    }

    [Fact]
    public async Task Upstream_no_store_responses_are_not_cached()
    {
        using var factory = new GatewayTestFactory();
        factory.Stub.Responder = static _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"once":"per-call"}""", Encoding.UTF8, "application/json"),
            };
            response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
            return response;
        };
        var client = factory.CreateClient();

        var first = await client.GetAsync("/api/no-store");
        var second = await client.GetAsync("/api/no-store");

        Assert.Equal("MISS", first.Headers.GetValues("X-Cache").Single());
        Assert.Equal("MISS", second.Headers.GetValues("X-Cache").Single());
        Assert.Equal(2, factory.Stub.CallCount);
    }
}
