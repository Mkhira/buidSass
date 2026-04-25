namespace BackendApi.Modules.Orders.Entities;

public sealed class QuotationLine
{
    public Guid Id { get; set; }
    public Guid QuotationId { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public int Qty { get; set; }
    public long UnitPriceMinor { get; set; }
    public long LineDiscountMinor { get; set; }
    public long LineTaxMinor { get; set; }
    public long LineTotalMinor { get; set; }
    public bool Restricted { get; set; }
    public string AttributesJson { get; set; } = "{}";

    public Quotation? Quotation { get; set; }
}
