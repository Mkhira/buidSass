using BackendApi.Modules.Cms.Services;

namespace BackendApi.Modules.Cms.Storefront.ListFeaturedSections;

public sealed record ListFeaturedSectionsResponse(
    IReadOnlyList<FeaturedSectionResponseItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record FeaturedSectionResponseItem(
    Guid SectionId,
    string SectionKind,
    string? Title,
    string? Subtitle,
    int DisplayPriority,
    string MarketCode,
    IReadOnlyList<ResolvedReference> ReferencesResolved,
    int TotalReferences,
    int TotalResolved,
    int TotalUnavailable,
    bool OmittedDueToUnavailableReferences);
