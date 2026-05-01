namespace BackendApi.Modules.Cms.Storefront.ListFeaturedSections;

public sealed record ListFeaturedSectionsQuery(
    string Market,
    string Locale,
    string? SectionKind,
    int Page,
    int PageSize);
