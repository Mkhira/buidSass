namespace BackendApi.Modules.Verification.Entities;

/// <summary>
/// Append-only ledger of every state transition. Postgres trigger forbids
/// UPDATE/DELETE so the audit-faithful history never drifts. See spec 020
/// data-model §2.3.
/// </summary>
public sealed class VerificationStateTransition
{
    public Guid Id { get; set; }
    public Guid VerificationId { get; set; }

    /// <summary>
    /// Market this transition belongs to (ADR-010 logical partitioning). Always
    /// equals the parent verification's market — denormalized here so retention,
    /// audit, and ops queries can scope by market without joining back to
    /// <c>verifications</c>.
    /// </summary>
    public string MarketCode { get; set; } = string.Empty;

    /// <summary>Use the literal <c>__none__</c> for the initial submission insert.</summary>
    public string PriorState { get; set; } = string.Empty;

    public string NewState { get; set; } = string.Empty;

    /// <summary>customer | reviewer | system.</summary>
    public string ActorKind { get; set; } = string.Empty;

    public Guid? ActorId { get; set; }

    /// <summary>Reviewer-entered reason (FR-014) or system reason code.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// jsonb. Holds e.g. <c>{ supersedes_id, document_ids[], reminder_window_days,
    /// idempotency_key }</c>. Defaults to <c>{}</c>.
    /// </summary>
    public string MetadataJson { get; set; } = "{}";

    public DateTimeOffset OccurredAt { get; set; }
}
