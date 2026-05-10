namespace BackendApi.Modules.Pricing.Admin.Preview;

/// <summary>
/// Spec 007-b Preview tool DTOs (contract §7.1). The "in-flight rule" is the
/// candidate the operator is authoring; the engine resolves the cart once
/// without it and once with it, and the response carries both plus the
/// per-line delta.
/// </summary>
public sealed record PreviewInFlightRule(
    string Kind,                 // "coupon" | "promotion" | "business_pricing" — only "coupon" supported in US1.
    Guid? RuleId,                // null when previewing an unsaved draft body.
    PreviewInFlightCouponBody? Coupon);

public sealed record PreviewInFlightCouponBody(
    string Code,
    string Type,                 // "percent_off" | "amount_off"
    int? Value,                  // bps when percent_off
    long? AmountOffMinor,        // minor units when amount_off
    long? CapMinor,
    bool ExcludesRestricted);

public sealed record PreviewExplanationRequest(
    Guid PreviewProfileId,
    PreviewInFlightRule InFlightRule);

public sealed record PreviewExplanationResponse(
    PreviewExplanationOutcome WithRule,
    PreviewExplanationOutcome WithoutRule,
    IReadOnlyList<PreviewLineDelta> DeltaPerLine,
    long ElapsedMs);

public sealed record PreviewExplanationOutcome(
    string ExplanationHash,
    IReadOnlyList<PreviewExplanationLine> Lines,
    PreviewExplanationTotals Totals);

public sealed record PreviewExplanationLine(
    Guid ProductId,
    string Sku,
    int Qty,
    long ListMinor,
    long NetMinor,
    long TaxMinor,
    long GrossMinor,
    IReadOnlyList<PreviewExplanationLayer> Explanation);

public sealed record PreviewExplanationLayer(
    string Layer,
    string? RuleId,
    string? RuleKind,
    long AppliedMinor,
    string? ReasonCode);

public sealed record PreviewExplanationTotals(
    long SubtotalMinor,
    long DiscountMinor,
    long TaxMinor,
    long GrandTotalMinor);

public sealed record PreviewLineDelta(
    Guid ProductId,
    string Sku,
    long DeltaMinor,
    string? Reason);
