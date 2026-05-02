using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Storefront.ListBlogArticles;

public sealed record ListBlogArticlesQuery(string Market, string Locale, string? Category, int Page, int PageSize);

public sealed record ListBlogArticlesResponse(IReadOnlyList<BlogArticleListItem> Items, int Page, int PageSize, bool HasMore);

public sealed record BlogArticleListItem(
    Guid Id,
    string Slug,
    string Category,
    string AuthoredLocale,
    string Title,
    string? Summary,
    Guid? CoverAssetId,
    string MarketCode,
    DateTimeOffset? PublishedAtUtc,
    IReadOnlyList<string> AvailableLocales,
    bool LocalizationUnavailableForRequestedLocale);

public sealed class ListBlogArticlesHandler
{
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    private readonly CmsDbContext _db;
    private readonly StorefrontContentResolver _resolver;
    private readonly TimeProvider _clock;

    public ListBlogArticlesHandler(CmsDbContext db, StorefrontContentResolver resolver, TimeProvider clock)
    {
        _db = db;
        _resolver = resolver;
        _clock = clock;
    }

    public async Task<ListBlogArticlesResponse> HandleAsync(ListBlogArticlesQuery query, CancellationToken ct)
    {
        var pageSize = Math.Clamp(query.PageSize <= 0 ? DefaultPageSize : query.PageSize, 1, MaxPageSize);
        // Clamp page so (page - 1) * pageSize never overflows int.MaxValue
        // even if a caller passes Page = int.MaxValue (CodeRabbit finding on PR #53).
        var page = Math.Clamp(query.Page <= 0 ? 1 : query.Page, 1, int.MaxValue / pageSize);

        // BlogArticle does not expose scheduled_start/end columns; apply
        // state + market directly here (see GetBlogArticleHandler note).
        IQueryable<BlogArticle> filtered = _db.BlogArticles.AsNoTracking()
            .Where(b => b.StateWire == "live")
            .Where(b => b.MarketCode == query.Market || b.MarketCode == "*");
        if (!string.IsNullOrEmpty(query.Category))
        {
            filtered = filtered.Where(b => b.CategoryWire == query.Category);
        }

        var rows = await filtered
            .OrderBy(b => b.MarketCode == query.Market ? 0 : 1)
            .ThenByDescending(b => b.PublishedAtUtc)
            .ThenByDescending(b => b.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var items = rows.Select(r =>
        {
            IReadOnlyList<string> availableLocales = new List<string> { r.AuthoredLocale };
            // Locale tags are case-insensitive per RFC 5646; treat ar / AR / en-US / en-us identically.
            var localeMissing = !string.Equals(r.AuthoredLocale, query.Locale, StringComparison.OrdinalIgnoreCase);
            return new BlogArticleListItem(
                r.Id,
                r.Slug,
                r.CategoryWire,
                r.AuthoredLocale,
                r.Title,
                r.Summary,
                r.CoverAssetId,
                r.MarketCode,
                r.PublishedAtUtc,
                availableLocales,
                localeMissing);
        }).ToList();

        return new ListBlogArticlesResponse(items, page, pageSize, hasMore);
    }
}
