namespace BackendApi.Modules.Pricing.Primitives;

public sealed record PriceResult(
    IReadOnlyList<PriceResultLine> Lines,
    PriceResultTotals Totals,
    string Currency,
    string ExplanationHash,
    Guid? ExplanationId);

public sealed record PriceResultLine(
    Guid ProductId,
    int Qty,
    long ListMinor,
    long NetMinor,
    long TaxMinor,
    long GrossMinor,
    IReadOnlyList<ExplanationRow> Layers);

public sealed record PriceResultTotals(
    long SubtotalMinor,
    long DiscountMinor,
    long TaxMinor,
    long GrandTotalMinor);
