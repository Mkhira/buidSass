namespace BackendApi.Modules.TaxInvoices.Entities;

/// <summary>Credit note (FR-008/FR-009). Refunds (spec 013) trigger issuance; references the
/// original invoice by id + number. Original invoice is never mutated (R7).</summary>
public sealed class CreditNote
{
    public const string StatePending = "pending";
    public const string StateRendered = "rendered";
    public const string StateDelivered = "delivered";
    public const string StateFailed = "failed";

    public Guid Id { get; set; }
    public string CreditNoteNumber { get; set; } = string.Empty;
    public Guid InvoiceId { get; set; }
    /// <summary>Per-market partitioning (Principle 5 / ADR-010) — denormalised from the parent invoice.</summary>
    public string MarketCode { get; set; } = string.Empty;
    public Guid? RefundId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public long SubtotalMinor { get; set; }
    public long DiscountMinor { get; set; }
    public long TaxMinor { get; set; }
    public long ShippingMinor { get; set; }
    public long GrandTotalMinor { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string? PdfBlobKey { get; set; }
    public string? PdfSha256 { get; set; }
    public string? ZatcaQrB64 { get; set; }
    public string State { get; set; } = StatePending;
    public int RenderAttempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Invoice? Invoice { get; set; }
    public List<CreditNoteLine> Lines { get; set; } = new();
}
