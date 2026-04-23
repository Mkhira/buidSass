namespace BackendApi.Modules.Pricing.Internal.Calculate;

public sealed record CalculateRequest(
    string? MarketCode,
    string? Locale,
    IReadOnlyList<CalculateLine>? Lines,
    string? CouponCode,
    Guid? QuotationId,
    Guid? OrderId,
    Guid? AccountId,
    string? Mode);

public sealed record CalculateLine(Guid ProductId, int Qty);

public sealed record CalculateResponse(
    IReadOnlyList<CalculateResponseLine> Lines,
    CalculateTotals Totals,
    string Currency,
    string ExplanationHash,
    Guid? ExplanationId);

public sealed record CalculateResponseLine(
    Guid ProductId,
    int Qty,
    long ListMinor,
    long NetMinor,
    long TaxMinor,
    long GrossMinor,
    IReadOnlyList<CalculateLayer> Layers);

public sealed record CalculateLayer(
    string Layer,
    string? RuleId,
    string? RuleKind,
    long AppliedMinor);

public sealed record CalculateTotals(
    long SubtotalMinor,
    long DiscountMinor,
    long TaxMinor,
    long GrandTotalMinor);
