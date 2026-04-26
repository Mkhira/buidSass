namespace BackendApi.Modules.Returns.Entities;

/// <summary>data-model.md table 2.</summary>
public sealed class ReturnLine
{
    public Guid Id { get; set; }
    public Guid ReturnRequestId { get; set; }
    /// <summary>Per-market partitioning (Principle 5 / ADR-010) — denormalised from the
    /// parent <see cref="ReturnRequest"/> so child queries can filter without a join.</summary>
    public string MarketCode { get; set; } = string.Empty;
    public Guid OrderLineId { get; set; }
    public int RequestedQty { get; set; }
    public int? ApprovedQty { get; set; }
    public int? ReceivedQty { get; set; }
    public int? SellableQty { get; set; }
    public int? DefectiveQty { get; set; }
    public string? LineReasonCode { get; set; }

    /// <summary>Pricing snapshot copied from the order line at submit-time so refund math
    /// stays reproducible if catalog/pricing changes later (research R6 / FR-014).</summary>
    public long UnitPriceMinor { get; set; }
    public long OriginalDiscountMinor { get; set; }
    /// <summary>Full <c>line_tax_minor</c> from the original order line. Pro-rated by qty
    /// at refund time so refund and credit-note totals reconcile (SC-009).</summary>
    public long OriginalTaxMinor { get; set; }
    public int TaxRateBp { get; set; }
    public int OriginalQty { get; set; }

    public ReturnRequest? ReturnRequest { get; set; }
}
