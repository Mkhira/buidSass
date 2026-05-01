namespace BackendApi.Modules.Cms.Storefront.ListBannerSlots;

public sealed record ListBannerSlotsResponse(
    IReadOnlyList<BannerSlotResponseItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record BannerSlotResponseItem(
    Guid Id,
    string SlotKind,
    string? Headline,
    string? Subhead,
    Guid? AssetId,
    string CtaKind,
    string? CtaTarget,
    string CtaHealth,
    DateTimeOffset? ScheduledStartUtc,
    DateTimeOffset? ScheduledEndUtc,
    int PriorityWithinSlot,
    string MarketCode);
