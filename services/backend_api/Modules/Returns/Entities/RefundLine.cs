namespace BackendApi.Modules.Returns.Entities;

/// <summary>data-model.md table 6. PK <c>(RefundId, ReturnLineId)</c>.</summary>
public sealed class RefundLine
{
    public Guid RefundId { get; set; }
    public Guid ReturnLineId { get; set; }
    public int Qty { get; set; }
    public long UnitPriceMinor { get; set; }
    public int TaxRateBp { get; set; }
    public long LineSubtotalMinor { get; set; }
    public long LineDiscountMinor { get; set; }
    public long LineTaxMinor { get; set; }
    public long LineAmountMinor { get; set; }

    public Refund? Refund { get; set; }
}
