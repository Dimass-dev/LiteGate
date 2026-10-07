using System.Text;
using LiteGate.Gateway.Configuration;

namespace LiteGate.Gateway.Caching;

/// <summary>Builds a stable cache key from the request method, path, query string and vary headers.</summary>
public static class CacheKeyBuilder
{
    public static string Build(HttpRequest request, CacheOptions options)
    {
        var builder = new StringBuilder("get|").Append(request.Path.Value ?? "/");
        if (request.QueryString.HasValue)
        {
            builder.Append('?').Append(request.QueryString.Value);
        }

        foreach (var headerName in options.VaryByHeaders.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (request.Headers.TryGetValue(headerName, out var values))
            {
                builder.Append('|').Append(headerName).Append('=').Append(values);
            }
        }

        return builder.ToString();
    }
}
