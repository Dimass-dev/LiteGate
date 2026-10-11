using LiteGate.Gateway.Configuration;
using LiteGate.Gateway.Observability;
using Microsoft.Extensions.Options;

namespace LiteGate.Gateway.Proxy;

/// <summary>Forwards incoming requests to the configured upstream and streams the response back.</summary>
public sealed class UpstreamForwarder(
    HttpMessageHandler handler,
    GatewayMetrics metrics,
    IOptions<GatewayOptions> options,
    ILogger<UpstreamForwarder> logger)
{
    private readonly HttpClient _httpClient = CreateHttpClient(handler, options.Value.Upstream);

    private static HttpClient CreateHttpClient(HttpMessageHandler handler, UpstreamOptions upstream) => new(handler, disposeHandler: false)
    {
        BaseAddress = new Uri(upstream.BaseAddress.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(Math.Max(1, upstream.TimeoutSeconds)),
    };

    public async Task ForwardAsync(HttpContext context)
    {
        using var upstreamRequest = BuildUpstreamRequest(context);

        HttpResponseMessage upstreamResponse;
        try
        {
            upstreamResponse = await _httpClient.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw; // the client went away; there is nobody left to answer
        }
        catch (TaskCanceledException)
        {
            metrics.IncrementUpstreamError();
            logger.LogError(
                "Upstream request timed out for {Method} {Path} after {TimeoutSeconds} s",
                context.Request.Method,
                context.Request.Path.Value,
                _httpClient.Timeout.TotalSeconds);
            await WriteGatewayErrorAsync(context, StatusCodes.Status504GatewayTimeout, "upstream_timeout");
            return;
        }
        catch (HttpRequestException exception)
        {
            metrics.IncrementUpstreamError();
            logger.LogError(
                exception,
                "Upstream request failed for {Method} {Path}",
                context.Request.Method,
                context.Request.Path.Value);
            await WriteGatewayErrorAsync(context, StatusCodes.Status502BadGateway, "upstream_unreachable");
            return;
        }

        using (upstreamResponse)
        {
            context.Response.StatusCode = (int)upstreamResponse.StatusCode;
            foreach (var (name, values) in upstreamResponse.Headers)
            {
                if (!HopByHopHeaders.Response.Contains(name))
                {
                    context.Response.Headers.Append(name, values.ToArray());
                }
            }
            foreach (var (name, values) in upstreamResponse.Content.Headers)
            {
                if (!HopByHopHeaders.Response.Contains(name))
                {
                    context.Response.Headers.Append(name, values.ToArray());
                }
            }

            await upstreamResponse.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
    }

    private static HttpRequestMessage BuildUpstreamRequest(HttpContext context)
    {
        var incoming = context.Request;
        var upstreamRequest = new HttpRequestMessage(
            new HttpMethod(incoming.Method),
            incoming.Path.Value + incoming.QueryString.Value);

        if (incoming.ContentLength is > 0 || incoming.Headers.TransferEncoding.Count > 0)
        {
            upstreamRequest.Content = new StreamContent(incoming.Body);
            if (!string.IsNullOrEmpty(incoming.ContentType))
            {
                upstreamRequest.Content.Headers.TryAddWithoutValidation("Content-Type", incoming.ContentType!);
            }
        }

        foreach (var header in incoming.Headers)
        {
            if (HopByHopHeaders.Request.Contains(header.Key) ||
                header.Key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!upstreamRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            {
                upstreamRequest.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        upstreamRequest.Headers.Add("X-Forwarded-For", context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        upstreamRequest.Headers.Add("X-Forwarded-Proto", incoming.Scheme);
        upstreamRequest.Headers.Add("X-Forwarded-Host", incoming.Host.Value);
        return upstreamRequest;
    }

    private static async Task WriteGatewayErrorAsync(HttpContext context, int statusCode, string errorCode)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new { error = errorCode }, context.RequestAborted);
    }
}
