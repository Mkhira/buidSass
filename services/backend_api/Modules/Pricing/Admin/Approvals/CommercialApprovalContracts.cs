using System.Text.Json.Serialization;

namespace BackendApi.Modules.Pricing.Admin.Approvals;

/// <summary>
/// Spec 007-b commercial approval-queue DTOs (US5 / contract §8). High-impact
/// drafts (per the per-market threshold row + FR-025) that an operator cannot
/// self-activate are listed here for the second-pair-of-eyes review.
/// </summary>
public sealed record PendingApprovalRow(
    [property: JsonPropertyName("target_entity_kind")] string TargetEntityKind, // coupon | promotion
    [property: JsonPropertyName("target_entity_id")] Guid TargetEntityId,
    [property: JsonPropertyName("author_actor_id")] Guid AuthorActorId,
    [property: JsonPropertyName("authored_at_utc")] DateTimeOffset AuthoredAtUtc,
    [property: JsonPropertyName("markets")] string[] Markets,
    [property: JsonPropertyName("display_label_ar")] string DisplayLabelAr,
    [property: JsonPropertyName("display_label_en")] string DisplayLabelEn,
    [property: JsonPropertyName("trigger_summary")] string TriggerSummary);

public sealed record PendingApprovalListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<PendingApprovalRow> Items);

public sealed record RecordApprovalRequest(
    [property: JsonPropertyName("target_entity_kind")] string TargetEntityKind,
    [property: JsonPropertyName("target_entity_id")] Guid TargetEntityId,
    [property: JsonPropertyName("cosign_note")] string CosignNote);

public sealed record RecordApprovalResponse(
    [property: JsonPropertyName("approval_id")] Guid ApprovalId,
    [property: JsonPropertyName("target_entity_kind")] string TargetEntityKind,
    [property: JsonPropertyName("target_entity_id")] Guid TargetEntityId,
    [property: JsonPropertyName("approver_actor_id")] Guid ApproverActorId,
    [property: JsonPropertyName("approved_at_utc")] DateTimeOffset ApprovedAtUtc,
    [property: JsonPropertyName("activated")] bool Activated,
    [property: JsonPropertyName("new_state")] string NewState);

public sealed record RejectApprovalRequest(
    [property: JsonPropertyName("target_entity_kind")] string TargetEntityKind,
    [property: JsonPropertyName("target_entity_id")] Guid TargetEntityId,
    [property: JsonPropertyName("reason_note")] string ReasonNote);
