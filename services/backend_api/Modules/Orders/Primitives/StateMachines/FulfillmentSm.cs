namespace BackendApi.Modules.Orders.Primitives.StateMachines;

/// <summary>
/// Fulfillment state machine. Spec 011 SM-3.
/// </summary>
public static class FulfillmentSm
{
    public const string NotStarted = "not_started";
    public const string AwaitingStock = "awaiting_stock";
    public const string Picking = "picking";
    public const string Packed = "packed";
    public const string HandedToCarrier = "handed_to_carrier";
    public const string Delivered = "delivered";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        NotStarted, AwaitingStock, Picking, Packed, HandedToCarrier, Delivered, Cancelled,
    };

    public static bool IsValidTransition(string from, string to)
    {
        // Cancellation is allowed from any non-terminal state — spec 011 SM-3 "any → cancelled"
        // when the order itself is cancelled. Delivered is terminal: a delivered fulfillment
        // CANNOT be reverted to cancelled (a return is the right path — spec 013).
        if (string.Equals(to, Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            return !string.Equals(from, Delivered, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(from, Cancelled, StringComparison.OrdinalIgnoreCase);
        }

        return (from, to) switch
        {
            (NotStarted, Picking) => true,
            (NotStarted, AwaitingStock) => true,
            (AwaitingStock, Picking) => true,
            (Picking, Packed) => true,
            (Packed, HandedToCarrier) => true,
            (HandedToCarrier, Delivered) => true,
            // Idempotent self-transitions absorb retried webhook deliveries.
            (var f, var t) when string.Equals(f, t, StringComparison.OrdinalIgnoreCase) => true,
            _ => false,
        };
    }
}
