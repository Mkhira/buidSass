using System.Diagnostics;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Search.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Search.Customer.SearchProducts;

public static class SearchProductsHandler
{
    public static async Task<SearchProductsHandlerResult> HandleAsync(
        SearchProductsRequest request,
        ISearchEngine searchEngine,
        CatalogDbContext catalogDbContext,
        QueryLogger queryLogger,
        CancellationToken cancellationToken)
    {
        var marketCode = request.MarketCode?.Trim().ToLowerInvariant() ?? "ksa";
        var locale = request.Locale?.Trim().ToLowerInvariant() ?? "en";

        if (!IndexNames.TryResolve(marketCode, locale, out var index))
        {
            return SearchProductsHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "search.market_locale_index_missing",
                "Unknown market/locale",
                "The requested market and locale index is not configured.");
        }

        var query = request.Query?.Trim() ?? string.Empty;
        var page = request.Page is null or < 1 ? 1 : request.Page.Value;
        var pageSize = request.PageSize is null or < 1 or > 100 ? 24 : request.PageSize.Value;

        if (string.IsNullOrWhiteSpace(query))
        {
            var fallback = await GetFeaturedFallbackAsync(catalogDbContext, marketCode, locale, page, pageSize, cancellationToken);
            queryLogger.Log(query, marketCode, locale, fallback.Hits.Count, fallback.QueryDurationMs, HasFilters(request.Filters));
            return SearchProductsHandlerResult.Success(fallback, localeFallbackApplied: false);
        }

        var sort = BuildSort(request.Sort);
        if (sort is null)
        {
            return SearchProductsHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "search.invalid_sort",
                "Invalid sort",
                "The requested sort key is not supported.");
        }

        var stopwatch = Stopwatch.StartNew();

        var filters = BuildFilters(request.Filters);
        var searchResponse = await searchEngine.SearchAsync(
            index.Name,
            new SearchEngineSearchRequest(
                query,
                page,
                pageSize,
                filters,
                sort,
                ["brandId", "categoryIds", "restricted", "availability"]),
            cancellationToken);

        stopwatch.Stop();

        var hits = searchResponse.Hits
            .Select(hit => new SearchProductHit(
                hit.Id,
                hit.Sku,
                hit.Barcode,
                hit.Name,
                hit.ShortDescription,
                hit.BrandId,
                hit.BrandName,
                hit.CategoryIds,
                hit.PriceHintMinorUnits,
                hit.Restricted,
                hit.RestrictionReasonCode,
                hit.Availability,
                hit.PrimaryMedia.ThumbUrl,
                hit.MarketCode,
                hit.Locale))
            .ToArray();

        var facets = BuildFacets(searchResponse.Facets, searchResponse.Hits);
        var queryDuration = (int)stopwatch.ElapsedMilliseconds;

        queryLogger.Log(query, marketCode, locale, hits.Length, queryDuration, filters.Count > 0);

        return SearchProductsHandlerResult.Success(
            new SearchProductsResponse(
                hits,
                facets,
                searchResponse.TotalEstimate,
                queryDuration,
                searchResponse.EngineLatencyMs,
                LocaleFallbackApplied: false),
            localeFallbackApplied: false);
    }

    private static async Task<SearchProductsResponse> GetFeaturedFallbackAsync(
        CatalogDbContext catalogDbContext,
        string marketCode,
        string locale,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var baseQuery = catalogDbContext.Products
            .AsNoTracking()
            .Where(p => p.Status == "published" && p.MarketCodes.Contains(marketCode));

        var total = await baseQuery.CountAsync(cancellationToken);

        var rows = await (
            from product in baseQuery
            join brand in catalogDbContext.Brands.AsNoTracking() on product.BrandId equals brand.Id
            select new
            {
                Product = product,
                BrandAr = brand.NameAr,
                BrandEn = brand.NameEn,
            })
            .OrderByDescending(x => x.Product.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var productIds = rows.Select(x => x.Product.Id).ToArray();
        var categoriesLookup = await catalogDbContext.ProductCategories
            .AsNoTracking()
            .Where(pc => productIds.Contains(pc.ProductId))
            .GroupBy(pc => pc.ProductId)
            .ToDictionaryAsync(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(x => x.CategoryId).ToArray(), cancellationToken);

        var mediaLookup = await catalogDbContext.ProductMedia
            .AsNoTracking()
            .Where(pm => productIds.Contains(pm.ProductId) && pm.IsPrimary)
            .GroupBy(pm => pm.ProductId)
            .ToDictionaryAsync(g => g.Key, g => g.OrderBy(x => x.DisplayOrder).Select(x => x.StorageKey).FirstOrDefault(), cancellationToken);

        var hits = rows.Select(row =>
        {
            var localizedName = locale == "ar" ? row.Product.NameAr : row.Product.NameEn;
            var localizedShort = locale == "ar" ? row.Product.ShortDescriptionAr : row.Product.ShortDescriptionEn;
            var brandName = locale == "ar" ? row.BrandAr : row.BrandEn;
            var thumb = mediaLookup.TryGetValue(row.Product.Id, out var storageKey) && storageKey is not null
                ? $"/v1/customer/catalog/media/{storageKey}"
                : null;

            return new SearchProductHit(
                row.Product.Id,
                row.Product.Sku,
                row.Product.Barcode,
                localizedName,
                localizedShort,
                row.Product.BrandId,
                brandName,
                categoriesLookup.GetValueOrDefault(row.Product.Id, []),
                row.Product.PriceHintMinorUnits,
                row.Product.Restricted,
                row.Product.RestrictionReasonCode,
                "in_stock",
                thumb,
                marketCode,
                locale);
        }).ToArray();

        return new SearchProductsResponse(
            hits,
            new SearchFacets(
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>()),
            total,
            QueryDurationMs: 0,
            EngineLatencyMs: 0,
            LocaleFallbackApplied: false);
    }

    private static IReadOnlyList<string>? BuildSort(string? sort)
    {
        var normalized = sort?.Trim().ToLowerInvariant();
        return normalized switch
        {
            null or "" or "relevance" => [],
            "price-asc" => ["priceHintMinorUnits:asc"],
            "price-desc" => ["priceHintMinorUnits:desc"],
            "newness" => ["publishedAt:desc"],
            "featured" => ["featuredAt:desc", "publishedAt:desc"],
            _ => null,
        };
    }

    private static IReadOnlyList<string> BuildFilters(SearchProductsFilters? filters)
    {
        if (filters is null)
        {
            return [];
        }

        var result = new List<string>();

        if (filters.BrandIds is { Length: > 0 })
        {
            var values = filters.BrandIds
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => $"brandId = \"{EscapeFilter(v)}\"")
                .ToArray();
            if (values.Length > 0)
            {
                result.Add($"({string.Join(" OR ", values)})");
            }
        }

        if (filters.CategoryIds is { Length: > 0 })
        {
            var values = filters.CategoryIds
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => $"categoryIds = \"{EscapeFilter(v)}\"")
                .ToArray();
            if (values.Length > 0)
            {
                result.Add($"({string.Join(" OR ", values)})");
            }
        }

        if (filters.PriceMinMinor is long min)
        {
            result.Add($"priceHintMinorUnits >= {min}");
        }

        if (filters.PriceMaxMinor is long max)
        {
            result.Add($"priceHintMinorUnits <= {max}");
        }

        if (!string.IsNullOrWhiteSpace(filters.Restricted))
        {
            var restricted = filters.Restricted.Trim().ToLowerInvariant();
            if (restricted == "only-restricted")
            {
                result.Add("restricted = true");
            }
            else if (restricted == "only-unrestricted")
            {
                result.Add("restricted = false");
            }
        }

        if (!string.IsNullOrWhiteSpace(filters.Availability)
            && !string.Equals(filters.Availability, "any", StringComparison.OrdinalIgnoreCase))
        {
            result.Add($"availability = \"{EscapeFilter(filters.Availability)}\"");
        }

        return result;
    }

    private static bool HasFilters(SearchProductsFilters? filters)
    {
        if (filters is null)
        {
            return false;
        }

        return (filters.BrandIds?.Length ?? 0) > 0
            || (filters.CategoryIds?.Length ?? 0) > 0
            || filters.PriceMinMinor is not null
            || filters.PriceMaxMinor is not null
            || !string.IsNullOrWhiteSpace(filters.Restricted)
            || !string.IsNullOrWhiteSpace(filters.Availability);
    }

    private static SearchFacets BuildFacets(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> engineFacets,
        IReadOnlyList<ProductSearchProjection> hits)
    {
        var brand = engineFacets.TryGetValue("brandId", out var brandFacet)
            ? brandFacet
            : new Dictionary<string, int>();
        var category = engineFacets.TryGetValue("categoryIds", out var categoryFacet)
            ? categoryFacet
            : new Dictionary<string, int>();
        var restricted = engineFacets.TryGetValue("restricted", out var restrictedFacet)
            ? restrictedFacet
            : new Dictionary<string, int>();
        var availability = engineFacets.TryGetValue("availability", out var availabilityFacet)
            ? availabilityFacet
            : new Dictionary<string, int>();

        var priceBucket = hits
            .Where(h => h.PriceHintMinorUnits.HasValue)
            .GroupBy(h => ToPriceBucket(h.PriceHintMinorUnits!.Value))
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return new SearchFacets(brand, category, priceBucket, restricted, availability);
    }

    private static string ToPriceBucket(long minorUnits)
    {
        return minorUnits switch
        {
            < 10_000 => "0-99",
            < 50_000 => "100-499",
            < 200_000 => "500-1999",
            _ => "2000+",
        };
    }

    private static string EscapeFilter(string value)
    {
        return value.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}

public sealed record SearchProductsHandlerResult(
    bool IsSuccess,
    SearchProductsResponse? Response,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail,
    bool LocaleFallbackApplied)
{
    public static SearchProductsHandlerResult Success(SearchProductsResponse response, bool localeFallbackApplied) =>
        new(true, response, StatusCodes.Status200OK, null, null, null, localeFallbackApplied);

    public static SearchProductsHandlerResult Fail(int statusCode, string reasonCode, string title, string detail) =>
        new(false, null, statusCode, reasonCode, title, detail, false);
}
