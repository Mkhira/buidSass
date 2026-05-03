namespace BackendApi.Modules.Cms.Editor.GetFeaturedSectionAdminDetail;

/// <summary>
/// Admin-side read for the featured-section authoring UI per spec 024 task T121.
/// Returns the row plus per-reference availability badges sourced from the
/// catalog read contracts (<see cref="BackendApi.Modules.Shared.ICatalogProductReadContract"/>
/// et al.) so editors can see broken refs before publish.
/// </summary>
public sealed record GetFeaturedSectionAdminDetailQuery(Guid Id);

public sealed record FeaturedSectionAdminReferenceItem(
    string Kind,
    Guid Id,
    string? DisplayNameEn,
    string? DisplayNameAr,
    bool IsAvailable,
    string? UnavailableReasonWire);

public sealed record FeaturedSectionAdminDetailResponse(
    Guid Id,
    string SectionKind,
    string? TitleAr,
    string? TitleEn,
    string? SubtitleAr,
    string? SubtitleEn,
    int DisplayPriority,
    string MarketCode,
    string State,
    DateTimeOffset? ScheduledPublishAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset EditorSaveAtUtc,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset? ArchivedAtUtc,
    Guid OwnerActorId,
    bool OwnershipOrphaned,
    int TotalReferences,
    int TotalAvailable,
    int TotalBroken,
    IReadOnlyList<FeaturedSectionAdminReferenceItem> References,
    uint Xmin);
