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
        // BlogArticle does not have scheduled_start/end columns (its
        // visibility is expressed via published_at_utc + state=live), so
        // we apply state + market directly here instead of the generic
        // resolver. Two-tier sort: specific market first, then `*`.
        var rows = await _db.BlogArticles.AsNoTracking()
            .Where(b => b.StateWire == "live")
            .Where(b => b.MarketCode == market || b.MarketCode == "*")
            .Where(b => b.Slug == slug)
            .OrderBy(b => b.MarketCode == market ? 0 : 1)
            .ThenByDescending(b => b.PublishedAtUtc)
            .ToListAsync(ct);
        var row = rows.FirstOrDefault();

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
