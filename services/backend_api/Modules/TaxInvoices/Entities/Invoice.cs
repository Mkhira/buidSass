namespace BackendApi.Modules.TaxInvoices.Entities;

/// <summary>
/// Tax-invoice aggregate root. Spec 012 data-model.md §1. Once <c>State = rendered</c> the
/// PDF blob and SHA are immutable (FR-010); admin <c>regenerate</c> mutates the SHA + bytes
/// but never the invoice number. Spec 013's refund issues a credit note instead.
/// </summary>
public sealed class Invoice
{
    public const string StatePending = "pending";
    public const string StateRendered = "rendered";
    public const string StateDelivered = "delivered";
    public const string StateFailed = "failed";

    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public Guid AccountId { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; set; }
    public Guid PriceExplanationId { get; set; }
    public long SubtotalMinor { get; set; }
    public long DiscountMinor { get; set; }
    public long TaxMinor { get; set; }
    public long ShippingMinor { get; set; }
    public long GrandTotalMinor { get; set; }
    public string BillToJson { get; set; } = "{}";
    public string SellerJson { get; set; } = "{}";
    public string? B2bPoNumber { get; set; }
    public string? PdfBlobKey { get; set; }
    public string? PdfSha256 { get; set; }
    public string? XmlBlobKey { get; set; }
    public string? ZatcaQrB64 { get; set; }
    public string State { get; set; } = StatePending;
    public int RenderAttempts { get; set; }
    public string? LastError { get; set; }
    public uint RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<InvoiceLine> Lines { get; set; } = new();
    public List<CreditNote> CreditNotes { get; set; } = new();
}
