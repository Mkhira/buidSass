namespace BackendApi.Modules.Pricing.Primitives;

public sealed record PricingContext(
    string MarketCode,
    string Locale,
    PricingAccountContext? Account,
    IReadOnlyList<PricingContextLine> Lines,
    string? CouponCode,
    Guid? QuotationId,
    Guid? OrderId,
    DateTimeOffset NowUtc,
    PricingMode Mode);

public sealed record PricingContextLine(
    Guid ProductId,
    int Qty,
    long ListPriceMinor,
    bool Restricted,
    IReadOnlyList<Guid> CategoryIds);

public sealed record PricingAccountContext(
    Guid AccountId,
    string? TierSlug,
    string VerificationState);

public enum PricingMode
{
    Preview,
    Issue,
}
