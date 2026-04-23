using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace BackendApi.Modules.Observability;

public sealed class SearchMetrics : IDisposable
{
    private readonly Meter _meter = new("BackendApi.Search");
    private readonly Histogram<double> _queryLatencyMs;
    private readonly ConcurrentDictionary<string, int> _lagByIndex = new(StringComparer.OrdinalIgnoreCase);

    public SearchMetrics()
    {
        _queryLatencyMs = _meter.CreateHistogram<double>("search_query_latency_ms", unit: "ms");
        _ = _meter.CreateObservableGauge(
            "search_indexer_lag_seconds",
            ObserveLag,
            unit: "s",
            description: "Indexer lag in seconds by search index.");
    }

    public void RecordQueryLatency(int latencyMs, string locale, bool hasFilters)
    {
        _queryLatencyMs.Record(
            Math.Max(0, latencyMs),
            new KeyValuePair<string, object?>("locale", locale),
            new KeyValuePair<string, object?>("has_filters", hasFilters));
    }

    public void ObserveIndexerLag(string indexName, int lagSeconds)
    {
        _lagByIndex[indexName] = Math.Max(0, lagSeconds);
    }

    public void Dispose()
    {
        _meter.Dispose();
    }

    private IEnumerable<Measurement<int>> ObserveLag()
    {
        foreach (var (indexName, lagSeconds) in _lagByIndex)
        {
            yield return new Measurement<int>(lagSeconds, new KeyValuePair<string, object?>("index_name", indexName));
        }
    }
}
