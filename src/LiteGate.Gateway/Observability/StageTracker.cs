using System.Diagnostics;

namespace LiteGate.Gateway.Observability;

public static class StageNames
{
    public const string RateLimit = "rate_limit";
    public const string CacheLookup = "cache_lookup";
    public const string CacheStore = "cache_store";
    public const string Upstream = "upstream";
}

/// <summary>
/// Measures wall-clock durations of the individual stages of a single request
/// and exposes them to the final structured log entry.
/// </summary>
public sealed class StageTracker(ILogger logger)
{
    public const string HttpContextItemsKey = "LiteGate.StageTracker";

    private readonly Dictionary<string, double> _stageDurationsMs = new(StringComparer.Ordinal);
    private readonly long _startedAtTimestamp = Stopwatch.GetTimestamp();

    public bool CacheHit { get; set; }

    public bool Throttled { get; set; }

    public StageScope BeginStage(string name) => new(this, name);

    public double TotalMs => ToMilliseconds(Stopwatch.GetTimestamp() - _startedAtTimestamp);

    public double GetStageMs(string name)
    {
        lock (_stageDurationsMs)
        {
            return _stageDurationsMs.TryGetValue(name, out var value) ? value : 0;
        }
    }

    private void EndStage(string name, long startedAtTimestamp)
    {
        var elapsedMs = Math.Round(ToMilliseconds(Stopwatch.GetTimestamp() - startedAtTimestamp), 3);
        lock (_stageDurationsMs)
        {
            _stageDurationsMs[name] = elapsedMs;
        }
        logger.LogDebug("Stage {Stage} finished in {ElapsedMs} ms", name, elapsedMs);
    }

    private static double ToMilliseconds(long timestampDelta) => timestampDelta * 1000d / Stopwatch.Frequency;

    public readonly struct StageScope(StageTracker tracker, string name) : IDisposable
    {
        private readonly long _startedAtTimestamp = Stopwatch.GetTimestamp();

        public void Dispose() => tracker.EndStage(name, _startedAtTimestamp);
    }
}
