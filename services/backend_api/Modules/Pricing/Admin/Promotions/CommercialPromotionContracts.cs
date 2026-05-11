namespace BackendApi.Modules.Pricing.Admin.Promotions;

/// <summary>
/// Spec 007-b Promotion admin DTOs (contract §3 — mirror of §2 with the
/// promotion-specific fields layered on top). Wire-shape only; persistence
/// shape lives on <see cref="BackendApi.Modules.Pricing.Entities.Promotion"/>.
/// </summary>
public sealed record CommercialPromotionBilingual(string Ar, string En);

public sealed record CreateCommercialPromotionRequest(
    string Kind,                                    // percent_off | amount_off | bogo | bundle
    string[] Markets,
    int? PercentOff,                                // 1..100 when Kind=percent_off
    long? AmountOffMinor,                           // > 0 when Kind=amount_off
    Guid? RewardSku,                                // required when Kind=bogo
    Guid? BundleSku,                                // required when Kind=bundle
    Guid[]? AppliesToProductIds,                    // SKU list, max 500
    Guid[]? AppliesToCategoryIds,
    int Priority,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool StacksWithCoupons,                         // default true
    bool StacksWithOtherPromotions,                 // default true
    bool BannerEligible,                            // default false
    CommercialPromotionBilingual? Label,
    CommercialPromotionBilingual? Description);

public sealed record UpdateCommercialPromotionRequest(
    string? Kind,
    int? PercentOff,
    long? AmountOffMinor,
    Guid? RewardSku,
    Guid? BundleSku,
    Guid[]? AppliesToProductIds,
    Guid[]? AppliesToCategoryIds,
    string[]? Markets,
    int? Priority,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool? StacksWithCoupons,
    bool? StacksWithOtherPromotions,
    bool? BannerEligible,
    CommercialPromotionBilingual? Label,
    CommercialPromotionBilingual? Description);

/// <summary>
/// Schedule body. The optional <paramref name="AcknowledgeOverlap"/> flag is
/// required when the non-stacking promotion overlaps existing rules (FR-016 /
/// contract §3 ack-loop). The first schedule call returns a 400 with overlap
/// rule ids; client re-posts with <c>acknowledge_overlap=true</c>.
/// </summary>
public sealed record CommercialPromotionScheduleRequest(bool? AcknowledgeOverlap);

public sealed record DeactivateOrReactivatePromotionRequest(string ReasonNote);

public sealed record CommercialPromotionResponse(
    Guid Id,
    string Kind,
    string State,
    string[] Markets,
    int? PercentOff,
    long? AmountOffMinor,
    Guid? RewardSku,
    Guid? BundleSku,
    Guid[]? AppliesToProductIds,
    Guid[]? AppliesToCategoryIds,
    int Priority,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool StacksWithCoupons,
    bool StacksWithOtherPromotions,
    bool BannerEligible,
    CommercialPromotionBilingual Label,
    CommercialPromotionBilingual? Description,
    uint RowVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset StateChangedAtUtc,
    Guid AuthorActorId,
    IReadOnlyList<CommercialPromotionAuditSummaryRow>? AuditSummary);

public sealed record CommercialPromotionAuditSummaryRow(
    string Kind,
    Guid ActorId,
    string ActorRole,
    DateTimeOffset RecordedAtUtc,
    string? ReasonNote);

public sealed record CommercialPromotionListResponse(
    IReadOnlyList<CommercialPromotionResponse> Items,
    string? NextCursor);
