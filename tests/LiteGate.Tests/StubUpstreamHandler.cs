using System.Net;
using System.Text;

namespace LiteGate.Tests;

/// <summary>In-process stand-in for the upstream service: records calls and returns canned responses.</summary>
public sealed class StubUpstreamHandler : HttpMessageHandler
{
    private int _callCount;

    public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }

    public Dictionary<string, string> LastRequestHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string LastRequestBody { get; private set; } = string.Empty;

    public int CallCount => Volatile.Read(ref _callCount);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        LastRequestHeaders.Clear();
        foreach (var header in request.Headers)
        {
            LastRequestHeaders[header.Key] = string.Join(", ", header.Value);
        }

        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        var response = Responder?.Invoke(request)
            ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"message":"ok"}""", Encoding.UTF8, "application/json"),
            };
        response.RequestMessage = request;
        return response;
    }
}
