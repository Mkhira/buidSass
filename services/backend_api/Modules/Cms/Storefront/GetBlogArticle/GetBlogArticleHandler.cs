using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Storefront.GetBlogArticle;

public sealed record GetBlogArticleResponse(
    Guid Id,
    string Slug,
    string Category,
    string AuthoredLocale,
    string Title,
    string? Summary,
    string? Body,
    Guid? CoverAssetId,
    string? SeoMetaTitle,
    string? SeoMetaDescription,
    Guid? SeoOgImageId,
    string SeoSchemaOrgKind,
    string MarketCode,
    DateTimeOffset? PublishedAtUtc,
    IReadOnlyList<string> AvailableLocales,
    bool LocalizationUnavailableForRequestedLocale);

public sealed class GetBlogArticleHandler
{
    private readonly CmsDbContext _db;
    private readonly StorefrontContentResolver _resolver;
    private readonly TimeProvider _clock;

    public GetBlogArticleHandler(CmsDbContext db, StorefrontContentResolver resolver, TimeProvider clock)
    {
        _db = db;
        _resolver = resolver;
        _clock = clock;
    }

    public async Task<GetBlogArticleResponse?> HandleAsync(
        string slug,
        string market,
        string locale,
        CancellationToken ct)
    {
        var filtered = _resolver.ApplyStorefrontFilter(
            _db.BlogArticles.AsNoTracking(),
            market,
            _clock.GetUtcNow());

        var row = await ((IQueryable<BackendApi.Modules.Cms.Entities.BlogArticle>)filtered)
            .Where(b => b.Slug == slug)
            .OrderByDescending(b => b.PublishedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;

        var localeMissing = !string.Equals(row.AuthoredLocale, locale, StringComparison.Ordinal);
        var bodyForLocale = localeMissing ? null : row.Body;

        return new GetBlogArticleResponse(
            row.Id,
            row.Slug,
            row.CategoryWire,
            row.AuthoredLocale,
            row.Title,
            row.Summary,
            bodyForLocale,
            row.CoverAssetId,
            row.SeoMetaTitle,
            row.SeoMetaDescription,
            row.SeoOgImageId,
            row.SeoSchemaOrgKind,
            row.MarketCode,
            row.PublishedAtUtc,
            new List<string> { row.AuthoredLocale },
            localeMissing);
    }
}
