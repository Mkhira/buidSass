namespace BackendApi.Modules.Shared;

/// <summary>
/// Bridge between Quotes/B2B (spec 021) and Orders (spec 011). Spec 021 owns the
/// quote-acceptance flow; Orders subscribes at this seam and creates the persistent order
/// row + reservation deduction + outbox emissions.
///
/// Lives in <c>Modules/Shared/</c> so 021 and 011 can depend on the contract without
/// forming a module dependency cycle (same pattern as
/// <see cref="IOrderFromCheckoutHandler"/> and <see cref="ICustomerPostSignInHook"/>).
///
/// Until spec 011 ships its implementation, 021 ships an in-process stub
/// (<c>StubOrderFromQuoteHandler</c> in <c>Tests/B2B.Tests/Fixtures/</c>) so the
/// acceptance flow is testable end-to-end. Production wiring lands with spec 011.
///
/// Idempotency contract (research §R6):
/// <list type="bullet">
///   <item>The implementer MUST be idempotent on <see cref="QuoteConversionRequest.IdempotencyKey"/>.</item>
///   <item>Replays MUST return the prior <see cref="OrderConversionResult.OrderId"/> with
///         <see cref="OrderConversionResult.WasIdempotentReplay"/> set <c>true</c>.</item>
///   <item>The implementer MUST enroll in the caller's ambient transaction so a partial
///         failure rolls back atomically (SC-007).</item>
/// </list>
/// </summary>
public interface IOrderFromQuoteHandler
{
    ValueTask<OrderConversionResult> CreateAsync(
        QuoteConversionRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Spec 021 → 011 conversion request. Lines + totals are pre-computed by the quote and
/// pinned to the accepted version; spec 011 trusts these values and does NOT re-price.
/// (Tax preview drift detection is the caller's responsibility — see research §R11.)
/// </summary>
public sealed record QuoteConversionRequest(
    Guid QuoteId,
    Guid CustomerId,
    Guid? CompanyId,
    Guid? CompanyBranchId,
    string MarketCode,
    string? PoNumber,
    bool InvoiceBilling,
    int? TermsDays,
    IReadOnlyList<QuoteConversionLine> Lines,
    decimal Subtotal,
    decimal TotalDiscount,
    decimal GrandTotal,
    Guid IdempotencyKey);

public sealed record QuoteConversionLine(
    string Sku,
    int Quantity,
    decimal UnitPrice,
    decimal LineDiscount,
    decimal LineTaxPreview);

public sealed record OrderConversionResult(
    Guid OrderId,
    bool WasIdempotentReplay);
