using BackendApi.Modules.Reviews.Primitives;

namespace BackendApi.Modules.Reviews.Entities;

/// <summary>
/// Append-only denormalized cache of every <see cref="Review"/> state transition
/// per data-model §2.2. Mirrors the canonical <c>shared.audit_log_entries</c>
/// row but is module-local for fast moderator-detail rendering.
/// </summary>
public sealed class ReviewModerationDecision
{
    public Guid Id { get; set; }
    public Guid ReviewId { get; set; }
    public Guid ActorId { get; set; }

    /// <summary>One of <c>customer</c>, <c>reviews.moderator</c>, <c>super_admin</c>, <c>system</c>.</summary>
    public string ActorRole { get; set; } = string.Empty;

    /// <summary>
    /// Prior state of the review at the time of this transition. <c>null</c>
    /// for the initial submission row (no prior state) — every other audit
    /// row MUST set this. (M3 — see PR #73.)
    /// </summary>
    public ReviewState? FromState { get; set; }
    public ReviewState ToState { get; set; }

    public string TriggeredBy { get; set; } = string.Empty;
    public string? ReasonNote { get; set; }
    public string? AdminNote { get; set; }

    /// <summary>jsonb snapshot of body/headline pre-transition (forensics).</summary>
    public string? BeforeJson { get; set; }
    /// <summary>jsonb snapshot of body/headline post-transition.</summary>
    public string? AfterJson { get; set; }

    public Guid? CorrelationId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
