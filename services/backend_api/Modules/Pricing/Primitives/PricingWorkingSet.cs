namespace BackendApi.Modules.Pricing.Primitives;

public sealed class PricingWorkingSet
{
    public PricingContext Context { get; }
    public List<WorkingLine> Lines { get; }
    public TaxRateSnapshot? TaxRate { get; set; }
    public AppliedCouponInfo? AppliedCoupon { get; set; }

    public PricingWorkingSet(PricingContext context, IEnumerable<WorkingLine> lines)
    {
        Context = context;
        Lines = [.. lines];
    }
}

public sealed class WorkingLine
{
    public Guid ProductId { get; }
    public int Qty { get; }
    public long ListMinor { get; }
    public bool Restricted { get; }
    public IReadOnlyList<Guid> CategoryIds { get; }

    /// <summary>Current net (after tier + promotion + coupon, before tax). Updated layer-by-layer.</summary>
    public long NetMinor { get; set; }

    /// <summary>Tax amount, populated by TaxLayer.</summary>
    public long TaxMinor { get; set; }

    public List<ExplanationRow> Explanation { get; } = [];

    public WorkingLine(Guid productId, int qty, long listMinor, bool restricted, IReadOnlyList<Guid> categoryIds)
    {
        ProductId = productId;
        Qty = qty;
        ListMinor = listMinor;
        Restricted = restricted;
        CategoryIds = categoryIds;
        NetMinor = 0;
    }
}

public sealed record TaxRateSnapshot(
    Guid Id,
    string MarketCode,
    string Kind,
    int RateBps);

public sealed record AppliedCouponInfo(
    Guid CouponId,
    string Code,
    string Kind,
    int Value,
    long? CapMinor,
    bool ExcludesRestricted);
