namespace BackendApi.Modules.Cms.Editor.SaveBannerDraft;

/// <summary>Request body for POST/PATCH /v1/admin/cms/banner-slots/drafts per spec 024 contract §3.1 / §3.2.</summary>
public sealed record SaveBannerDraftRequest(
    string SlotKind,
    string? HeadlineAr,
    string? HeadlineEn,
    string? SubheadAr,
    string? SubheadEn,
    Guid? AssetIdAr,
    Guid? AssetIdEn,
    string CtaKind,
    string? CtaTarget,
    DateTimeOffset? ScheduledStartUtc,
    DateTimeOffset? ScheduledEndUtc,
    string MarketCode,
    int? PriorityWithinSlot,
    uint? Xmin);
