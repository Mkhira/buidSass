namespace BackendApi.Modules.TaxInvoices.Entities;

public sealed class InvoiceLine
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid OrderLineId { get; set; }
    /// <summary>Per-market partitioning (Principle 5 / ADR-010) — denormalised from the parent invoice.</summary>
    public string MarketCode { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public int Qty { get; set; }
    public long UnitPriceMinor { get; set; }
    public long LineDiscountMinor { get; set; }
    public long LineTaxMinor { get; set; }
    public long LineTotalMinor { get; set; }
    /// <summary>Tax rate in basis points (e.g. 1500 = 15 %).</summary>
    public int TaxRateBp { get; set; }

    public Invoice? Invoice { get; set; }
}
