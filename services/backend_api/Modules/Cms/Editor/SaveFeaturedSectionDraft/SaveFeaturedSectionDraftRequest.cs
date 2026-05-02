namespace BackendApi.Modules.Cms.Editor.SaveFeaturedSectionDraft;

public sealed record FeaturedReference(string Kind, Guid Id);

public sealed record SaveFeaturedSectionDraftRequest(
    string SectionKind,
    string? TitleAr,
    string? TitleEn,
    string? SubtitleAr,
    string? SubtitleEn,
    IReadOnlyList<FeaturedReference> References,
    int? DisplayPriority,
    string MarketCode,
    DateTimeOffset? ScheduledPublishAtUtc,
    uint? Xmin);
