namespace BackendApi.Modules.Shared;

/// <summary>
/// Bridge between Checkout (spec 010) and Orders (spec 011). Spec 010 owns the checkout flow
/// but NOT order creation — Orders subscribes at this seam. Lives in `Modules/Shared/` so
/// Checkout and Orders can depend on the contract without forming a module cycle (same
/// pattern as <see cref="ICustomerPostSignInHook"/>).
///
/// Until spec 011 lands, Checkout ships a minimal in-process stub implementation that skips
/// order creation and returns a deterministic placeholder so the Submit flow is testable
/// end-to-end. Spec 011 replaces the stub with the real handler that owns order rows,
/// reservation → deduction conversion (spec 008), and outbox emissions (`payment.captured`,
/// `order.placed`).
/// </summary>
public interface IOrderFromCheckoutHandler
{
    Task<OrderFromCheckoutResult> CreateAsync(OrderFromCheckoutRequest request, CancellationToken cancellationToken);
}

public sealed record OrderFromCheckoutRequest(
    /// <summary>Pre-allocated order id — Checkout generates it before calling Pricing Issue mode so the explanation row has a stable owner (`pricing.issue_requires_owner`). Spec 011 MUST honour this id.</summary>
    Guid PreallocatedOrderId,
    Guid SessionId,
    Guid CartId,
    Guid AccountId,
    string MarketCode,
    IReadOnlyList<OrderFromCheckoutLine> Lines,
    string? CouponCode,
    string PaymentMethod,
    string? PaymentProviderId,
    string? PaymentProviderTxnId,
    long ShippingFeeMinor,
    string ShippingProviderId,
    string ShippingMethodCode,
    string ShippingAddressJson,
    string BillingAddressJson,
    long SubtotalMinor,
    long DiscountMinor,
    long TaxMinor,
    long GrandTotalMinor,
    string Currency,
    Guid IssuedExplanationId);

public sealed record OrderFromCheckoutLine(
    Guid ProductId,
    int Qty,
    long UnitPriceMinor,
    long NetMinor,
    long TaxMinor,
    long GrossMinor,
    Guid? ReservationId);

public sealed record OrderFromCheckoutResult(
    bool IsSuccess,
    Guid? OrderId,
    string? OrderNumber,
    string? PaymentState,      // "captured" | "pending" | "pending_cod"
    string? ErrorCode = null,
    string? ErrorMessage = null);
