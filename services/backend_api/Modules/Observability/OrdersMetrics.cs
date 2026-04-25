using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BackendApi.Modules.Observability;

/// <summary>
/// I2 — explicit ActivitySource for orders flows. Attached to <c>orders.create_from_checkout</c>
/// and <c>orders.outbox.dispatch</c> spans so traces show the parent
/// <c>checkout.confirm → order.placed</c> link clearly.
/// </summary>
public static class OrdersTracing
{
    public const string ActivitySourceName = "DentalCommerce.Orders";
    public static readonly ActivitySource Source = new(ActivitySourceName);
}

/// <summary>
/// I1 — orders module metrics. Mirrors <see cref="InventoryMetrics"/> / <see cref="SearchMetrics"/>
/// shapes. Histogram for create latency (SC-001), counters for the SC-005 dedup hits and
/// fulfillment state-transition trail.
/// </summary>
public sealed class OrdersMetrics
{
    public const string MeterName = "DentalCommerce.Orders";

    private readonly Counter<long> _ordersCreated;
    private readonly Counter<long> _ordersCancelled;
    private readonly Counter<long> _webhookDedupHits;
    private readonly Counter<long> _fulfillmentTransitions;
    private readonly Counter<long> _paymentTransitions;
    private readonly Histogram<double> _createDurationMs;
    private readonly Histogram<double> _outboxDispatchMs;

    public OrdersMetrics()
    {
        var meter = new Meter(MeterName);
        _ordersCreated = meter.CreateCounter<long>("orders.created_total", description: "Orders created (FR-001).");
        _ordersCancelled = meter.CreateCounter<long>("orders.cancelled_total", description: "Orders cancelled (FR-004).");
        _webhookDedupHits = meter.CreateCounter<long>("orders.webhook_dedup_hits", description: "SC-005 dedup hits at the order seam.");
        _fulfillmentTransitions = meter.CreateCounter<long>("orders.fulfillment.state_transitions", description: "Fulfillment state-machine transitions.");
        _paymentTransitions = meter.CreateCounter<long>("orders.payment.state_transitions", description: "Payment state-machine transitions.");
        _createDurationMs = meter.CreateHistogram<double>("orders.create_duration_ms", description: "Order creation latency (SC-001).");
        _outboxDispatchMs = meter.CreateHistogram<double>("orders.outbox_dispatch_ms", description: "Outbox dispatcher per-batch duration.");
    }

    public void IncrementCreated(string market) =>
        _ordersCreated.Add(1, new KeyValuePair<string, object?>("market", market));

    public void IncrementCancelled(string market, string paymentState) =>
        _ordersCancelled.Add(1,
            new KeyValuePair<string, object?>("market", market),
            new KeyValuePair<string, object?>("payment_state", paymentState));

    public void IncrementWebhookDedupHit(string providerId) =>
        _webhookDedupHits.Add(1, new KeyValuePair<string, object?>("provider", providerId));

    public void IncrementFulfillmentTransition(string fromState, string toState) =>
        _fulfillmentTransitions.Add(1,
            new KeyValuePair<string, object?>("from", fromState),
            new KeyValuePair<string, object?>("to", toState));

    public void IncrementPaymentTransition(string fromState, string toState) =>
        _paymentTransitions.Add(1,
            new KeyValuePair<string, object?>("from", fromState),
            new KeyValuePair<string, object?>("to", toState));

    public void RecordCreateDuration(double ms, string market, string outcome) =>
        _createDurationMs.Record(ms,
            new KeyValuePair<string, object?>("market", market),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordOutboxDispatchDuration(double ms, int batchSize) =>
        _outboxDispatchMs.Record(ms, new KeyValuePair<string, object?>("batch_size", batchSize));
}
