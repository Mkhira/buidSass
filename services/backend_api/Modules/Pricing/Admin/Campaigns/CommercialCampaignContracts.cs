using System.Text.Json.Serialization;

namespace BackendApi.Modules.Pricing.Admin.Campaigns;

/// <summary>
/// Spec 007-b commercial Campaign DTOs (US4 / contract §5). A Campaign is a
/// bilingual merchandising container linked to exactly one Promotion or Coupon
/// (or a landing-only entry). Lifecycle uses the shared <c>LifecycleState</c>
/// machine; high-impact gate does NOT apply to campaigns (per data-model §3.1
/// — the gate is coupon/promotion-only).
/// </summary>
public sealed record CampaignBilingual(
    [property: JsonPropertyName("ar")] string Ar,
    [property: JsonPropertyName("en")] string En);

public sealed record CampaignLinkRequest(
    [property: JsonPropertyName("kind")] string Kind,        // promotion | coupon | landing_only
    [property: JsonPropertyName("target_id")] Guid? TargetId);

public sealed record CreateCampaignRequest(
    [property: JsonPropertyName("name")] CampaignBilingual? Name,
    [property: JsonPropertyName("valid_from")] DateTimeOffset ValidFrom,
    [property: JsonPropertyName("valid_to")] DateTimeOffset ValidTo,
    [property: JsonPropertyName("markets")] string[] Markets,
    [property: JsonPropertyName("landing_query")] string? LandingQuery,
    [property: JsonPropertyName("notes_internal")] string? NotesInternal,
    [property: JsonPropertyName("campaign_link")] CampaignLinkRequest? CampaignLink);

public sealed record UpdateCampaignRequest(
    [property: JsonPropertyName("name")] CampaignBilingual? Name,
    [property: JsonPropertyName("valid_from")] DateTimeOffset? ValidFrom,
    [property: JsonPropertyName("valid_to")] DateTimeOffset? ValidTo,
    [property: JsonPropertyName("markets")] string[]? Markets,
    [property: JsonPropertyName("landing_query")] string? LandingQuery,
    [property: JsonPropertyName("notes_internal")] string? NotesInternal,
    [property: JsonPropertyName("campaign_link")] CampaignLinkRequest? CampaignLink);

public sealed record CampaignLinkResponse(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("target_id")] Guid? TargetId,
    [property: JsonPropertyName("link_broken_at_utc")] DateTimeOffset? LinkBrokenAtUtc);

public sealed record CommercialCampaignResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("name")] CampaignBilingual Name,
    [property: JsonPropertyName("valid_from")] DateTimeOffset ValidFrom,
    [property: JsonPropertyName("valid_to")] DateTimeOffset ValidTo,
    [property: JsonPropertyName("markets")] string[] Markets,
    [property: JsonPropertyName("landing_query")] string? LandingQuery,
    [property: JsonPropertyName("notes_internal")] string? NotesInternal,
    [property: JsonPropertyName("link_broken")] bool LinkBroken,
    [property: JsonPropertyName("campaign_link")] CampaignLinkResponse? CampaignLink,
    [property: JsonPropertyName("row_version")] uint RowVersion,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("state_changed_at_utc")] DateTimeOffset StateChangedAtUtc,
    [property: JsonPropertyName("audit_summary")] IReadOnlyList<CampaignAuditSummaryRow>? AuditSummary);

public sealed record CampaignAuditSummaryRow(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("actor_id")] Guid ActorId,
    [property: JsonPropertyName("actor_role")] string ActorRole,
    [property: JsonPropertyName("recorded_at_utc")] DateTimeOffset RecordedAtUtc,
    [property: JsonPropertyName("reason_note")] string? ReasonNote);

public sealed record CommercialCampaignListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<CommercialCampaignResponse> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

public sealed record CampaignLookupRow(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] CampaignBilingual Name,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("markets")] string[] Markets,
    [property: JsonPropertyName("valid_from")] DateTimeOffset ValidFrom,
    [property: JsonPropertyName("valid_to")] DateTimeOffset ValidTo,
    [property: JsonPropertyName("link_kind")] string? LinkKind,
    [property: JsonPropertyName("link_target_id")] Guid? LinkTargetId);

public sealed record CampaignLookupResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<CampaignLookupRow> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

public sealed record DeactivateCampaignRequest(
    [property: JsonPropertyName("reason_note")] string ReasonNote);
