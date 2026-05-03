namespace BackendApi.Modules.B2B.Entities;

/// <summary>
/// Spec 021 append-only quote state transition ledger (data-model §2.8). One row per
/// state change; UPDATE / DELETE are blocked by a Postgres BEFORE trigger
/// (asserted by <c>StateTransitionAppendOnlyTriggerTests</c>).
///
/// The initial insert uses the literal sentinel <c>__none__</c> for
/// <see cref="PriorState"/> so the timeline starts cleanly without modeling a magic
/// "pre-existence" enum value.
/// </summary>
public sealed class QuoteStateTransition
{
    public Guid Id { get; set; }
    public Guid QuoteId { get; set; }

    /// <summary>Two-letter market code (mirrors the parent <c>Quote.MarketCode</c>); ADR-010 partitioning.</summary>
    public string MarketCode { get; set; } = string.Empty;

    /// <summary>Either a <see cref="Primitives.QuoteState"/> token or the literal <c>__none__</c>.</summary>
    public string PriorState { get; set; } = string.Empty;

    /// <summary>A <see cref="Primitives.QuoteState"/> token.</summary>
    public string NewState { get; set; } = string.Empty;

    /// <summary>A <see cref="Primitives.QuoteActorKind"/> token.</summary>
    public string ActorKind { get; set; } = string.Empty;

    /// <summary>NULL for system transitions; otherwise the customer / admin / approver user-id.</summary>
    public Guid? ActorId { get; set; }

    /// <summary><c>{ en?, ar? }</c> — set for actions carrying a reason (revisions, approver rejection); NULL for system.</summary>
    public string? ReasonJson { get; set; }

    /// <summary>
    /// Default <c>{}</c>. Holds e.g. <c>{ idempotency_key, version_number, drift_pct,
    /// po_warning_acknowledged, revoked_by }</c>.
    /// </summary>
    public string MetadataJson { get; set; } = "{}";

    public DateTimeOffset OccurredAt { get; set; }
}
