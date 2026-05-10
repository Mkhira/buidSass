namespace BackendApi.Modules.Pricing.Admin.Coupons;

/// <summary>
/// Spec 007-b Coupon admin DTOs (contract §2). Wire-shape only; persistence
/// shape lives on <see cref="BackendApi.Modules.Pricing.Entities.Coupon"/>.
/// </summary>
public sealed record CommercialCouponBilingual(string Ar, string En);

public sealed record CreateCommercialCouponRequest(
    string Code,
    string[] Markets,
    string Type,                                // percent_off | amount_off
    int? Value,                                 // bps when percent_off
    long? AmountOffMinor,                       // single market authoring uses this; multi-market shipping reuses on the same row
    long? CapMinor,
    int? PerCustomerLimit,
    int? OverallLimit,
    bool ExcludesRestricted,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool StacksWithPromotions,
    bool DisplayInBanners,
    CommercialCouponBilingual? Label,
    CommercialCouponBilingual? Description);

public sealed record UpdateCommercialCouponRequest(
    string? Type,
    int? Value,
    long? AmountOffMinor,
    long? CapMinor,
    int? PerCustomerLimit,
    int? OverallLimit,
    bool? ExcludesRestricted,
    string[]? Markets,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool? StacksWithPromotions,
    bool? DisplayInBanners,
    CommercialCouponBilingual? Label,
    CommercialCouponBilingual? Description);

public sealed record DeactivateOrReactivateRequest(string ReasonNote);

public sealed record CommercialCouponResponse(
    Guid Id,
    string Code,
    string State,
    string Type,
    int? Value,
    long? CapMinor,
    int? PerCustomerLimit,
    int? OverallLimit,
    bool ExcludesRestricted,
    string[] Markets,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool DisplayInBanners,
    CommercialCouponBilingual Label,
    CommercialCouponBilingual? Description,
    uint RowVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset StateChangedAtUtc,
    Guid AuthorActorId,
    IReadOnlyList<CommercialAuditSummaryRow>? AuditSummary);

public sealed record CommercialAuditSummaryRow(
    string Kind,
    Guid ActorId,
    string ActorRole,
    DateTimeOffset RecordedAtUtc,
    string? ReasonNote);

public sealed record CommercialCouponListResponse(
    IReadOnlyList<CommercialCouponResponse> Items,
    string? NextCursor);
