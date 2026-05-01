namespace BackendApi.Modules.Cms.Storefront.ListBannerSlots;

public sealed record ListBannerSlotsQuery(
    string Market,
    string Locale,
    string? SlotKind,
    int Page,
    int PageSize);
