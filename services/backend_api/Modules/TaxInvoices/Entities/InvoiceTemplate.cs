namespace BackendApi.Modules.TaxInvoices.Entities;

/// <summary>
/// Per-market template configuration (FR-017). Razor templates compile at build time; this
/// row carries the runtime per-market overrides (legal entity, VAT, address, footer, bank).
/// </summary>
public sealed class InvoiceTemplate
{
    public string MarketCode { get; set; } = string.Empty;
    public string SellerLegalNameAr { get; set; } = string.Empty;
    public string SellerLegalNameEn { get; set; } = string.Empty;
    public string SellerVatNumber { get; set; } = string.Empty;
    public string SellerAddressAr { get; set; } = string.Empty;
    public string SellerAddressEn { get; set; } = string.Empty;
    /// <summary>JSON: { iban, bankNameAr, bankNameEn, accountHolder }.</summary>
    public string BankDetailsJson { get; set; } = "{}";
    public string? FooterHtmlAr { get; set; }
    public string? FooterHtmlEn { get; set; }
    public Guid? UpdatedByAccountId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
