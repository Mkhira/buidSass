namespace BackendApi.Modules.TaxInvoices.Entities;

public sealed class CreditNoteLine
{
    public Guid Id { get; set; }
    public Guid CreditNoteId { get; set; }
    public Guid InvoiceLineId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public int Qty { get; set; }
    public long UnitPriceMinor { get; set; }
    public long LineDiscountMinor { get; set; }
    public long LineTaxMinor { get; set; }
    public long LineTotalMinor { get; set; }
    public int TaxRateBp { get; set; }

    public CreditNote? CreditNote { get; set; }
}
