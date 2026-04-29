using BackendApi.Modules.Verification.Primitives;

namespace BackendApi.Modules.Verification.Entities;

/// <summary>
/// One row per verification submission. Customer may have multiple over time
/// (rejection → cooldown → re-submit, or expiry → renewal). See spec 020
/// data-model §2.1.
/// </summary>
public sealed class Verification
{
    public Guid Id { get; set; }

    /// <summary>
    /// FK → identity.customer_accounts.id (logical only — cross-module via
    /// <c>Modules/Shared/</c> queries; no DB-level FK across module schemas).
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>"eg" or "ksa".</summary>
    public string MarketCode { get; set; } = string.Empty;

    /// <summary>
    /// Snapshot pointer into <c>verification_market_schemas (market_code, version)</c>.
    /// Reviewers see the schema as the customer saw it at submission (FR-026).
    /// </summary>
    public int SchemaVersion { get; set; }

    public string Profession { get; set; } = string.Empty;

    /// <summary>
    /// PII — license number / regulator registration. NEVER logged. Read access
    /// gated by <see cref="VerificationPermissions.ReadPii"/> and recorded by
    /// <c>IPiiAccessRecorder</c>.
    /// </summary>
    public string RegulatorIdentifier { get; set; } = string.Empty;

    public VerificationState State { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public Guid? DecidedBy { get; set; }

    /// <summary>Set on <see cref="VerificationState.Approved"/>. Recomputed on renewal approval.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Renewal back-pointer (FR-020). Set on the renewal row at submission.</summary>
    public Guid? SupersedesId { get; set; }

    /// <summary>Forward-pointer once a renewal is approved (set in same Tx as the prior row's <see cref="VerificationState.Superseded"/>).</summary>
    public Guid? SupersededById { get; set; }

    /// <summary>Free text for <see cref="VerificationState.Void"/> transitions.</summary>
    public string? VoidReason { get; set; }

    /// <summary>
    /// jsonb capture of the <c>IProductRestrictionPolicy</c> view at submission
    /// time. Used by the reviewer queue + audit replay (FR-026).
    /// </summary>
    public string RestrictionPolicySnapshotJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>EF row-version (Postgres xmin) — optimistic concurrency token (R4).</summary>
    public uint Xmin { get; set; }
}
