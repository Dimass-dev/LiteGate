using System.Collections.Concurrent;
using LiteGate.Gateway.Configuration;
using Microsoft.Extensions.Options;

namespace LiteGate.Gateway.Observability;

/// <summary>In-process counters for status codes, cache efficiency and rolling latency percentiles.</summary>
public sealed class GatewayMetrics(IOptions<GatewayOptions> options)
{
    private readonly int _latencyWindowSize = Math.Max(16, options.Value.Observability.LatencySampleWindowSize);
    private readonly object _latencySync = new();
    private readonly Queue<double> _latencySamples = new();
    private readonly ConcurrentDictionary<int, long> _statusCounts = new();
    private long _totalRequests;
    private long _throttledRequests;
    private long _cacheHits;
    private long _cacheMisses;
    private long _upstreamErrors;

    public void RecordRequest(int statusCode, double totalMs)
    {
        Interlocked.Increment(ref _totalRequests);
        _statusCounts.AddOrUpdate(statusCode, 1, static (_, current) => current + 1);
        lock (_latencySync)
        {
            _latencySamples.Enqueue(totalMs);
            while (_latencySamples.Count > _latencyWindowSize)
            {
                _latencySamples.Dequeue();
            }
        }
    }

    public void IncrementCacheHit() => Interlocked.Increment(ref _cacheHits);

    public void IncrementCacheMiss() => Interlocked.Increment(ref _cacheMisses);

    public void IncrementThrottled() => Interlocked.Increment(ref _throttledRequests);

    public void IncrementUpstreamError() => Interlocked.Increment(ref _upstreamErrors);

    public MetricsSnapshot Snapshot()
    {
        double[] sortedSamples;
        lock (_latencySync)
        {
            sortedSamples = [.. _latencySamples];
        }

        Array.Sort(sortedSamples);

        return new MetricsSnapshot(
            Interlocked.Read(ref _totalRequests),
            Interlocked.Read(ref _throttledRequests),
            Interlocked.Read(ref _cacheHits),
            Interlocked.Read(ref _cacheMisses),
            Interlocked.Read(ref _upstreamErrors),
            _statusCounts.ToDictionary(static pair => pair.Key.ToString(), static pair => pair.Value),
            new LatencySnapshot(
                sortedSamples.Length,
                Percentile(sortedSamples, 50),
                Percentile(sortedSamples, 95),
                Percentile(sortedSamples, 99)));
    }

    private static double Percentile(double[] sortedSamples, double percentile)
    {
        if (sortedSamples.Length == 0)
        {
            return 0;
        }

        var index = Math.Clamp((int)Math.Ceiling(percentile / 100d * sortedSamples.Length) - 1, 0, sortedSamples.Length - 1);
        return Math.Round(sortedSamples[index], 3);
    }
}

public sealed record MetricsSnapshot(
    long TotalRequests,
    long ThrottledRequests,
    long CacheHits,
    long CacheMisses,
    long UpstreamErrors,
    IReadOnlyDictionary<string, long> ResponsesByStatus,
    LatencySnapshot LatencyMs);

public sealed record LatencySnapshot(int SampleCount, double P50, double P95, double P99);
