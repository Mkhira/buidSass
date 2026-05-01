namespace BackendApi.Modules.Cms.Editor.SaveBlogArticleDraft;

/// <summary>Request body for POST/PATCH /v1/admin/cms/blog-articles/drafts per spec 024 contract §3.</summary>
public sealed record SaveBlogArticleDraftRequest(
    string Category,
    string Slug,
    string AuthoredLocale,
    string Title,
    string? Summary,
    string? Body,
    Guid? CoverAssetId,
    string? SeoMetaTitle,
    string? SeoMetaDescription,
    Guid? SeoOgImageId,
    string? SeoSchemaOrgKind,
    DateTimeOffset? ScheduledPublishAtUtc,
    string MarketCode,
    uint? Xmin);
