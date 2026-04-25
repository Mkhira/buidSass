namespace BackendApi.Modules.Orders.Primitives.StateMachines;

/// <summary>
/// Payment state machine for the order aggregate (Principle 17). Spec 011 SM-2.
/// Distinct from spec 010's <c>PaymentAttemptStates</c>: that machine tracks the per-attempt
/// lifecycle on a single gateway call; this machine tracks the order-level invariant
/// ("does this order have money committed against it?"). Webhooks update both.
/// </summary>
public static class PaymentSm
{
    public const string Authorized = "authorized";
    public const string Captured = "captured";
    public const string PendingCod = "pending_cod";
    public const string PendingBankTransfer = "pending_bank_transfer";
    public const string PendingBnpl = "pending_bnpl";
    public const string Failed = "failed";
    public const string Voided = "voided";
    public const string Refunded = "refunded";
    public const string PartiallyRefunded = "partially_refunded";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Authorized, Captured, PendingCod, PendingBankTransfer, PendingBnpl,
        Failed, Voided, Refunded, PartiallyRefunded,
    };

    public static bool IsValidTransition(string from, string to) => (from, to) switch
    {
        (Authorized, Captured) => true,                   // webhook / manual capture
        (Authorized, Voided) => true,                     // cancellation pre-capture
        (Authorized, Failed) => true,                     // late decline post-authorize
        (Captured, Refunded) => true,                     // full refund (spec 013)
        (Captured, PartiallyRefunded) => true,            // partial refund
        (PartiallyRefunded, Refunded) => true,            // additional refund covers remainder
        (PartiallyRefunded, PartiallyRefunded) => true,   // additional partial refund (idempotent self)
        (PendingCod, Captured) => true,                   // delivery confirmation (FR-026, SC-008)
        (PendingCod, Failed) => true,                     // delivery failure
        (PendingBankTransfer, Captured) => true,          // admin confirm (FR-025)
        (PendingBankTransfer, Failed) => true,            // timeout / admin reject
        (PendingBnpl, Captured) => true,                  // BNPL provider authorize → capture
        (PendingBnpl, Failed) => true,                    // BNPL declined / abandoned
        (PendingBnpl, Voided) => true,                    // customer cancel pre-capture
        // Idempotent self-transitions tolerate duplicate webhook deliveries (SC-005).
        (var f, var t) when string.Equals(f, t, StringComparison.OrdinalIgnoreCase) => true,
        _ => false,
    };

    /// <summary>True if the state means "money has been collected" (for high-level status + invoice trigger).</summary>
    public static bool IsCaptured(string state) =>
        string.Equals(state, Captured, StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, PartiallyRefunded, StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, Refunded, StringComparison.OrdinalIgnoreCase);

    /// <summary>True if the state is pending external confirmation (no money in yet).</summary>
    public static bool IsPending(string state) =>
        string.Equals(state, PendingCod, StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, PendingBankTransfer, StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, PendingBnpl, StringComparison.OrdinalIgnoreCase);
}
