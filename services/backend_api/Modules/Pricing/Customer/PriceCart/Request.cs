namespace BackendApi.Modules.Pricing.Customer.PriceCart;

public sealed record PriceCartRequest(
    string? MarketCode,
    string? Locale,
    IReadOnlyList<PriceCartLine>? Lines,
    string? CouponCode);

public sealed record PriceCartLine(Guid ProductId, int Qty);

public sealed record PriceCartResponse(
    IReadOnlyList<PriceCartResponseLine> Lines,
    PriceCartTotals Totals,
    string Currency,
    string ExplanationHash);

public sealed record PriceCartResponseLine(
    Guid ProductId,
    int Qty,
    long ListMinor,
    long NetMinor,
    long TaxMinor,
    long GrossMinor,
    IReadOnlyList<PriceCartLayer> Layers);

public sealed record PriceCartLayer(
    string Layer,
    string? RuleId,
    string? RuleKind,
    long AppliedMinor);

public sealed record PriceCartTotals(
    long SubtotalMinor,
    long DiscountMinor,
    long TaxMinor,
    long GrandTotalMinor);
