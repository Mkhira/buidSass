namespace BackendApi.Modules.Shared;

/// <summary>
/// Spec 021 → spec 007-a pricing baseline + tax preview hook (research §R5). Used by
/// admin authoring (US3) to populate per-line baseline_unit_price + applicable
/// promotions + tax preview when the operator builds a quote version.
///
/// Bulk by design — admin authoring loads N SKUs at once (typically up to ~50 lines per
/// quote). The implementer SHOULD batch-fetch internally rather than N+1.
///
/// Tax preview is informational only. Authoritative tax is computed at order-conversion
/// time (spec 011 / 007-a Issue mode); the quote captures the preview so the customer
/// PDF can show what they were quoted. Tax-preview drift threshold gates the conversion
/// per <c>QuoteMarketSchema.TaxPreviewDriftThresholdPct</c>.
/// </summary>
public interface IPricingBaselineProvider
{
    ValueTask<IReadOnlyDictionary<string, PricingBaseline>> GetBaselinesAsync(
        Guid customerId,
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken);
}

public sealed record PricingBaseline(
    string Sku,
    decimal BaselineUnitPrice,
    string Currency,
    IReadOnlyList<AppliedPromotion> Promotions,
    decimal TaxPreviewUnitAmount,
    decimal TaxPreviewRatePct);

public sealed record AppliedPromotion(
    string PromotionCode,
    string LocalizedDescriptionKey,
    decimal LineDiscountAmount);
