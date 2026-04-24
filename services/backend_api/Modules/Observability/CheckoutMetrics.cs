using System.Diagnostics.Metrics;

namespace BackendApi.Modules.Observability;

/// <summary>
/// Histogram + counters for the Checkout module (spec 010 T035). `checkout_submit_duration_ms`
/// is the SLO metric: p95 goal = 3s (spec 010 NFR-PERF-SUBMIT).
/// </summary>
public sealed class CheckoutMetrics : IDisposable
{
    private readonly Meter _meter = new("BackendApi.Checkout");
    private readonly Histogram<double> _submitDurationMs;
    private readonly Counter<long> _submitOutcomes;
    private readonly Counter<long> _driftEvents;

    public CheckoutMetrics()
    {
        _submitDurationMs = _meter.CreateHistogram<double>(
            "checkout_submit_duration_ms",
            unit: "ms",
            description: "End-to-end duration of Submit handler in milliseconds. Tagged by market + outcome.");

        _submitOutcomes = _meter.CreateCounter<long>(
            "checkout_submit_outcomes_total",
            unit: "submits",
            description: "Submit handler outcomes (confirmed | failed | declined | drift | idempotent_hit).");

        _driftEvents = _meter.CreateCounter<long>(
            "checkout_pricing_drift_total",
            unit: "events",
            description: "Number of submits that tripped the pricing-drift gate.");
    }

    public void RecordSubmitDuration(double ms, string marketCode, string outcome)
    {
        _submitDurationMs.Record(
            Math.Max(0, ms),
            new KeyValuePair<string, object?>("market_code", marketCode),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void IncrementOutcome(string marketCode, string outcome)
    {
        _submitOutcomes.Add(
            1,
            new KeyValuePair<string, object?>("market_code", marketCode),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void IncrementDrift(string marketCode)
    {
        _driftEvents.Add(
            1,
            new KeyValuePair<string, object?>("market_code", marketCode));
    }

    public void Dispose() => _meter.Dispose();
}
