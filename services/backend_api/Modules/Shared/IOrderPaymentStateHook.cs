namespace BackendApi.Modules.Shared;

/// <summary>
/// Bridge between Checkout's payment-gateway webhook (spec 010) and the Orders aggregate
/// (spec 011 FR-007 / FR-024 / SC-005). When Checkout's webhook successfully advances a
/// PaymentAttempt, it invokes this hook so Orders can advance the corresponding Order's
/// <c>payment_state</c> (e.g., <c>authorized → captured</c>) and emit the downstream
/// <c>payment.captured</c> outbox event consumed by spec 012's invoice issuance.
///
/// Lives in <c>Modules/Shared/</c> so Checkout and Orders never form a module dependency
/// cycle (same pattern as <see cref="IOrderFromCheckoutHandler"/>). Until spec 011 lands,
/// Checkout ships a no-op stub; spec 011 replaces it with the real handler.
/// </summary>
public interface IOrderPaymentStateHook
{
    Task<OrderPaymentAdvanceResult> AdvanceFromAttemptAsync(
        OrderPaymentAdvanceRequest request,
        CancellationToken cancellationToken);
}

public sealed record OrderPaymentAdvanceRequest(
    string ProviderId,
    string ProviderTxnId,
    /// <summary>The PaymentAttempt state the webhook just transitioned into. Allowed values:
    /// <c>initiated|authorized|captured|declined|voided|failed|refunded|pending_webhook</c>.
    /// Orders maps this to its own <c>payment_state</c> domain values.</summary>
    string MappedAttemptState,
    string? ErrorCode,
    string? ErrorMessage,
    string? ProviderEventId);

public sealed record OrderPaymentAdvanceResult(
    /// <summary>True if a matching order was found and a transition was applied (or was a no-op
    /// idempotent self-transition). False if no matching order was found — caller logs + 200s.</summary>
    bool MatchedOrder,
    /// <summary>The order's payment_state AFTER applying. Null if no order matched.</summary>
    string? FinalPaymentState,
    Guid? OrderId);
