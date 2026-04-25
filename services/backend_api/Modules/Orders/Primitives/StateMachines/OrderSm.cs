namespace BackendApi.Modules.Orders.Primitives.StateMachines;

/// <summary>
/// Order state machine (Principle 17 — order/payment/fulfillment/refund are independent).
/// Spec 011 data-model.md SM-1.
/// States: <see cref="Placed"/>, <see cref="CancellationPending"/>, <see cref="Cancelled"/>.
/// </summary>
public static class OrderSm
{
    public const string Placed = "placed";
    public const string CancellationPending = "cancellation_pending";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Placed, CancellationPending, Cancelled,
    };

    /// <summary>Allowed transitions per spec 011 data-model.md SM-1.</summary>
    public static bool IsValidTransition(string from, string to) => (from, to) switch
    {
        (Placed, CancellationPending) => true,            // customer cancel, payment captured → refund pending
        (Placed, Cancelled) => true,                      // customer cancel (payment authorized) OR admin cancel
        (CancellationPending, Cancelled) => true,         // refund completed
        // Idempotent self-transitions tolerate retried external events landing twice.
        (var f, var t) when string.Equals(f, t, StringComparison.OrdinalIgnoreCase) => true,
        _ => false,
    };
}
