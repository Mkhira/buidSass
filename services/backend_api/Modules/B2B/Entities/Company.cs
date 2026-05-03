namespace BackendApi.Modules.B2B.Entities;

/// <summary>
/// Spec 021 company-account aggregate root (data-model §2.1).
///
/// Single-vendor in V1; <see cref="Company"/> rows are not vendor-scoped because the
/// company entity describes the buying side, not the selling side. (Vendor-scoping for
/// quotes themselves arrives in Phase 2 via <c>quotes.vendor_id</c>.)
/// </summary>
public sealed class Company
{
    public Guid Id { get; set; }

    /// <summary>Bilingual display name: <c>{ "en": "...", "ar": "..." }</c>. Both locales required at registration (Principle 4).</summary>
    public string NameJson { get; set; } = string.Empty;

    /// <summary>Per-market regulator tax identifier. PII; never logged.</summary>
    public string TaxId { get; set; } = string.Empty;

    /// <summary>Two-letter market code: <c>eg</c> or <c>ksa</c> (lowercase per data-model §2.1).</summary>
    public string MarketCode { get; set; } = string.Empty;

    /// <summary>Structured primary address jsonb.</summary>
    public string PrimaryAddressJson { get; set; } = string.Empty;

    /// <summary>Structured billing address jsonb. <c>null</c> means "use primary".</summary>
    public string? BillingAddressJson { get; set; }

    /// <summary>When true, buyer acceptance routes through the company's approver(s).</summary>
    public bool ApproverRequired { get; set; } = true;

    /// <summary>When true, every quote-acceptance MUST carry a PO number.</summary>
    public bool PoRequired { get; set; }

    /// <summary>When true, PO numbers MUST be unique per company (deterministic via partial unique index on <c>quotes</c>).</summary>
    public bool UniquePoRequired { get; set; }

    /// <summary>Net-X invoicing eligibility; toggled off by spec 019 admin action.</summary>
    public bool InvoiceBillingEligible { get; set; } = true;

    /// <summary>One of <c>active | pending-verification | suspended | closed</c>.</summary>
    public string State { get; set; } = "active";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Postgres <c>xmin</c> mapped via <c>IsRowVersion()</c> for optimistic concurrency.</summary>
    public uint XminRowVersion { get; set; }
}
