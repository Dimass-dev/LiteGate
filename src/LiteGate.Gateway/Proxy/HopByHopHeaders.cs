namespace LiteGate.Gateway.Proxy;

/// <summary>
/// Headers that must not travel across a single proxy hop (RFC 7230 §6.1),
/// plus entries managed by the gateway itself.
/// </summary>
public static class HopByHopHeaders
{
    /// <summary>
    /// Stripped from the forwarded request. <c>Accept-Encoding</c> is stripped as well so the
    /// upstream answers with an uncompressed body the response cache can store verbatim.
    /// </summary>
    public static readonly IReadOnlySet<string> Request = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Transfer-Encoding",
        "Upgrade",
        "Proxy-Connection",
        "Proxy-Authorization",
        "Proxy-Authenticate",
        "TE",
        "Trailer",
        "Host",
        "Accept-Encoding",
    };

    /// <summary>Stripped from the upstream response before it is written back to the client.</summary>
    public static readonly IReadOnlySet<string> Response = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Transfer-Encoding",
        "Upgrade",
        "Proxy-Connection",
        "Proxy-Authenticate",
        "TE",
        "Trailer",
        "Content-Length",
    };
}
