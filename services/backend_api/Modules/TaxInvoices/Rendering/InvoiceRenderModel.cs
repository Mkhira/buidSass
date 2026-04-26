namespace BackendApi.Modules.TaxInvoices.Rendering;

/// <summary>
/// Strongly-typed render model fed to the bilingual HTML/PDF template. Captured snapshot
/// of the invoice + template + computed lines so renders are reproducible (FR-005 — taxes
/// are NOT recomputed; we use the stored explanation values).
/// </summary>
public sealed record InvoiceRenderModel(
    string InvoiceNumber,
    string OrderNumber,
    string MarketCode,
    string Currency,
    DateTimeOffset IssuedAt,
    string SellerLegalNameAr,
    string SellerLegalNameEn,
    string SellerVatNumber,
    string SellerAddressAr,
    string SellerAddressEn,
    string BillToAr,
    string BillToEn,
    string? B2bPoNumber,
    string? BuyerVatNumber,
    long SubtotalMinor,
    long DiscountMinor,
    long TaxMinor,
    long ShippingMinor,
    long GrandTotalMinor,
    string? FooterHtmlAr,
    string? FooterHtmlEn,
    string? BankNameAr,
    string? BankNameEn,
    string? Iban,
    /// <summary>Base64 ZATCA TLV — KSA only. Renderer encodes as a QR image.</summary>
    string? ZatcaQrB64,
    IReadOnlyList<InvoiceRenderLine> Lines,
    /// <summary>True for credit notes — renderer flips title + makes amounts negative.</summary>
    bool IsCreditNote = false,
    string? CreditNoteOriginalInvoiceNumber = null);

public sealed record InvoiceRenderLine(
    int Number,
    string Sku,
    string NameAr,
    string NameEn,
    int Qty,
    long UnitPriceMinor,
    long LineDiscountMinor,
    long LineTaxMinor,
    long LineTotalMinor,
    int TaxRateBp);
