namespace BackendApi.Modules.Shipping.Primitives;

/// <summary>
/// Canonical Shipment state names per <c>data-model.md</c> §state-machine.
/// Used in DB CHECK constraints, EF configurations, and the state machine.
/// </summary>
public static class ShipmentStates
{
    public const string Pending = "pending";
    public const string LabelPurchased = "label_purchased";
    public const string HandedToCarrier = "handed_to_carrier";
    public const string InTransit = "in_transit";
    public const string OutForDelivery = "out_for_delivery";
    public const string Delivered = "delivered";
    public const string DeliveryAttempted = "delivery_attempted";
    public const string ReturnToSenderInitiated = "return_to_sender_initiated";
    public const string ReturnedToSender = "returned_to_sender";
    public const string DeliveryDisputed = "delivery_disputed";
    public const string ReDeliveredPending = "re_delivered_pending";
    public const string ClosedWithRefund = "closed_with_refund";
    public const string FailedToCreateLabel = "failed_to_create_label";
    public const string PendingLabelProviderFailure = "pending_label_provider_failure";
    public const string DeadLetterLabel = "dead_letter_label";
    public const string LabelVoided = "label_voided";

    /// <summary>All valid Shipment.state values (anchor for the DB CHECK).</summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Pending,
            LabelPurchased,
            HandedToCarrier,
            InTransit,
            OutForDelivery,
            Delivered,
            DeliveryAttempted,
            ReturnToSenderInitiated,
            ReturnedToSender,
            DeliveryDisputed,
            ReDeliveredPending,
            ClosedWithRefund,
            FailedToCreateLabel,
            PendingLabelProviderFailure,
            DeadLetterLabel,
            LabelVoided,
        };

    /// <summary>
    /// Precedence per <c>BR-12</c>: out-of-order webhooks ranked here win.
    /// Higher value = more advanced; a lower-precedence event never regresses
    /// state (recorded in <c>shipment_events</c> history but not applied).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> Precedence =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [Pending] = 0,
            [LabelPurchased] = 1,
            [HandedToCarrier] = 2,
            [InTransit] = 3,
            [OutForDelivery] = 4,
            [DeliveryAttempted] = 5,
            [ReturnToSenderInitiated] = 6,
            [ReturnedToSender] = 7,
            [Delivered] = 8,
            // Terminal / off-path states do not participate in webhook ordering.
            [DeliveryDisputed] = -1,
            [ReDeliveredPending] = -1,
            [ClosedWithRefund] = -1,
            [FailedToCreateLabel] = -1,
            [PendingLabelProviderFailure] = -1,
            [DeadLetterLabel] = -1,
            [LabelVoided] = -1,
        };

    public static int PrecedenceOf(string state) =>
        Precedence.TryGetValue(state, out var p) ? p : -1;

    public static bool IsTerminal(string state) =>
        state is Delivered or ReturnedToSender or ClosedWithRefund or DeadLetterLabel or LabelVoided;

    public static bool IsActive(string state) =>
        state is Pending or LabelPurchased or HandedToCarrier
              or InTransit or OutForDelivery or DeliveryAttempted
              or ReturnToSenderInitiated or DeliveryDisputed or ReDeliveredPending
              or PendingLabelProviderFailure;
}

/// <summary>
/// Method-version lifecycle states per <c>data-model.md</c>
/// §shipping_method_versions.state.
/// </summary>
public static class MethodVersionStates
{
    public const string Draft = "draft";
    public const string InReview = "in_review";
    public const string Published = "published";
    public const string Archived = "archived";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Draft, InReview, Published, Archived };
}

/// <summary>Dispute-row status enum per <c>shipment_disputes.status</c>.</summary>
public static class ShipmentDisputeStatuses
{
    public const string Open = "open";
    public const string ReDelivered = "re_delivered";
    public const string ClosedWithRefund = "closed_with_refund";
    public const string ClosedNoAction = "closed_no_action";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Open, ReDelivered, ClosedWithRefund, ClosedNoAction };
}

/// <summary>Dead-letter resolution kinds per <c>dead_letter_labels.resolution</c>.</summary>
public static class DeadLetterResolutions
{
    public const string Retry = "retry";
    public const string Discard = "discard";
    public const string ManualLabel = "manual_label";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Retry, Discard, ManualLabel };
}
