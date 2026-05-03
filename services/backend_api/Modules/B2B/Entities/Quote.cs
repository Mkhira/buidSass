namespace BackendApi.Modules.B2B.Entities;

/// <summary>
/// Spec 021 Quote aggregate root (data-model §2.5). Lifecycle managed by
/// <see cref="Primitives.QuoteStateMachine"/>; concurrency guarded by <see cref="XminRowVersion"/>
/// (multi-approver finalize-race per SC-009 / FR-029).
/// </summary>
public sealed class Quote
{
    public Guid Id { get; set; }

    /// <summary>Logical FK to spec 004's customer table.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>NULL for individual-customer quotes (US2). FK to <see cref="Company"/> otherwise.</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>NULL = company HQ default; meaningful only when <see cref="CompanyId"/> is non-null.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>Captured at request; cross-market not allowed (FR-011). Lowercase token.</summary>
    public string MarketCode { get; set; } = string.Empty;

    /// <summary>One of <see cref="Primitives.QuoteState"/>'s tokens.</summary>
    public string State { get; set; } = "requested";

    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>Set on first publish; points to the most recent <see cref="QuoteVersion"/>.</summary>
    public Guid? CurrentVersionId { get; set; }

    /// <summary>Navigation for <see cref="CurrentVersionId"/>; explicit FK with restrict-on-delete.</summary>
    public QuoteVersion? CurrentVersion { get; set; }

    /// <summary>Navigation for <see cref="CompanyId"/>; null for individual-customer quotes.</summary>
    public Company? Company { get; set; }

    /// <summary>Navigation for <see cref="BranchId"/>; null when the company has no branch attribution.</summary>
    public CompanyBranch? Branch { get; set; }

    /// <summary>
    /// Set on first publish; recomputed when a revision sets <c>validity_extends=true</c>
    /// (Clarifications Q5).
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>NULL when system-driven (e.g. expired).</summary>
    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? TerminalAt { get; set; }

    /// <summary>Stable enum token aligned with a <see cref="Primitives.QuoteReasonCode"/> value.</summary>
    public string? TerminalReason { get; set; }

    /// <summary>Buyer-supplied PO number (NULL until provided at request or acceptance).</summary>
    public string? PoNumber { get; set; }

    public bool InvoiceBilling { get; set; }

    /// <summary>Customer-supplied jsonb message: <c>{ "en"?: "...", "ar"?: "..." }</c>.</summary>
    public string? CustomerSuppliedMessageJson { get; set; }

    public string? InternalNote { get; set; }

    /// <summary>Most recent approver rejection reason; cleared on next admin revision publish.</summary>
    public string? ApproverRejectionNote { get; set; }

    /// <summary>Snapshot of cart contents when this quote was requested from a cart (US1). NULL otherwise.</summary>
    public string? OriginatingCartSnapshotJson { get; set; }

    /// <summary>Set when the quote was requested from a single product (US2).</summary>
    public Guid? OriginatingProductId { get; set; }

    /// <summary>
    /// Snapshot of <c>IProductRestrictionPolicy</c> per-line at request time (mirrors spec
    /// 020 pattern). Reserves a future <c>vendor_id</c> slot for Phase 2.
    /// </summary>
    public string RestrictionPolicySnapshotJson { get; set; } = "{}";

    /// <summary>FK to <c>quote_market_schemas (market_code, version)</c>.</summary>
    public int SchemaVersion { get; set; }

    public uint XminRowVersion { get; set; }
}
