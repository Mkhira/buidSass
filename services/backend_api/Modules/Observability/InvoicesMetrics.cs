using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BackendApi.Modules.Observability;

/// <summary>
/// Spec 012 module metrics + tracing. Mirrors <see cref="OrdersMetrics"/> shape: counters for
/// the issuance / render / failure events, render-duration histogram, and a dedicated
/// ActivitySource for trace spans (<c>invoices.issue_on_capture</c>, <c>invoices.render</c>,
/// <c>invoices.credit_note.issue</c>).
/// </summary>
public sealed class InvoicesMetrics
{
    public const string MeterName = "DentalCommerce.Invoices";

    private readonly Counter<long> _issued;
    private readonly Counter<long> _rendered;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _creditNotesIssued;
    private readonly Counter<long> _subscriberSkipped;
    private readonly Histogram<double> _renderDurationMs;

    public InvoicesMetrics()
    {
        var meter = new Meter(MeterName);
        _issued = meter.CreateCounter<long>("invoices.issued_total", description: "Invoices issued (FR-001).");
        _rendered = meter.CreateCounter<long>("invoices.rendered_total", description: "Invoices successfully rendered.");
        _failed = meter.CreateCounter<long>("invoices.failed_total", description: "Invoice renders that hit max attempts.");
        _creditNotesIssued = meter.CreateCounter<long>("invoices.credit_notes_issued_total",
            description: "Credit notes issued (FR-008).");
        _subscriberSkipped = meter.CreateCounter<long>("invoices.subscriber_skipped_total",
            description: "payment.captured events skipped by the subscriber (legitimate state-mismatch).");
        _renderDurationMs = meter.CreateHistogram<double>("invoices.render_duration_ms",
            description: "PDF render p50/p95/p99 (SC-001 target ≤ 3 s p95).");
    }

    public void IncrementIssued(string market) =>
        _issued.Add(1, new KeyValuePair<string, object?>("market", market));

    public void IncrementRendered(string market) =>
        _rendered.Add(1, new KeyValuePair<string, object?>("market", market));

    public void IncrementFailed(string market, string reason) =>
        _failed.Add(1,
            new KeyValuePair<string, object?>("market", market),
            new KeyValuePair<string, object?>("reason", reason));

    public void IncrementCreditNoteIssued(string market) =>
        _creditNotesIssued.Add(1, new KeyValuePair<string, object?>("market", market));

    public void IncrementSubscriberSkipped(string reason) =>
        _subscriberSkipped.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void RecordRenderDuration(double ms, string market, string outcome) =>
        _renderDurationMs.Record(ms,
            new KeyValuePair<string, object?>("market", market),
            new KeyValuePair<string, object?>("outcome", outcome));
}

public static class InvoicesTracing
{
    public const string ActivitySourceName = "DentalCommerce.Invoices";
    public static readonly ActivitySource Source = new(ActivitySourceName);
}
