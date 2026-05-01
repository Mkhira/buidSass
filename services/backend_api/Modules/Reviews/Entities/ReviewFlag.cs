namespace BackendApi.Modules.Reviews.Entities;

/// <summary>
/// Community report against a review per data-model §2.4. Append-only;
/// idempotent per <c>(review_id, reporter_actor_id)</c>. <see cref="IsQualified"/>
/// is captured at report time per FR-023 / R5 so threshold-evaluation outcomes
/// are reproducible during dispute audit.
/// </summary>
public sealed class ReviewFlag
{
    public Guid Id { get; set; }
    public Guid ReviewId { get; set; }
    public Guid ReporterActorId { get; set; }

    /// <summary>One of the 5 fixed reasons from contract §2.5.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Required + length-checked at app layer when <see cref="Reason"/> = <c>other_with_required_note</c>.</summary>
    public string? Note { get; set; }

    public bool IsQualified { get; set; }

    /// <summary>jsonb capture of <c>{ account_age_days, has_delivered_order, schema_id }</c> at report time.</summary>
    public string QualifyingEvaluationJson { get; set; } = "{}";

    public DateTimeOffset CreatedAtUtc { get; set; }
}
