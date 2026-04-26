namespace BackendApi.Modules.Returns.Primitives;

/// <summary>
/// SM-1. ReturnRequest state machine — FR-004, SC-004.
/// </summary>
public static class ReturnStateMachine
{
    public const string PendingReview = "pending_review";
    public const string Approved = "approved";
    public const string ApprovedPartial = "approved_partial";
    public const string Rejected = "rejected";
    public const string Received = "received";
    public const string Inspected = "inspected";
    public const string Refunded = "refunded";
    public const string RefundFailed = "refund_failed";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        PendingReview, Approved, ApprovedPartial, Rejected, Received, Inspected, Refunded, RefundFailed,
    };

    public static bool IsValidTransition(string from, string to)
    {
        var f = from?.ToLowerInvariant() ?? string.Empty;
        var t = to?.ToLowerInvariant() ?? string.Empty;
        return (f, t) switch
        {
            (PendingReview, Approved) => true,
            (PendingReview, ApprovedPartial) => true,
            (PendingReview, Rejected) => true,
            (PendingReview, Refunded) => true,                           // force-refund (skip-physical)
            (Approved, Received) => true,
            (ApprovedPartial, Received) => true,
            (Received, Inspected) => true,
            (Inspected, Refunded) => true,
            (Inspected, RefundFailed) => true,
            (RefundFailed, Refunded) => true,
            // Idempotent self-transition absorbs duplicate event deliveries.
            (var ff, var tt) when ff == tt => true,
            _ => false,
        };
    }
}
