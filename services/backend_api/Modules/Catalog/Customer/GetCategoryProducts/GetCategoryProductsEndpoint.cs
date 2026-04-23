using BackendApi.Modules.Catalog.Customer.Common;
using BackendApi.Modules.Catalog.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Catalog.Customer.GetCategoryProducts;

public static class GetCategoryProductsEndpoint
{
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/categories/{slug}/products", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        string slug,
        string? market,
        int? page,
        int? pageSize,
        string? sort,
        string? brand,
        long? priceMin,
        long? priceMax,
        string? restricted,
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var marketCode = CustomerCatalogResponseFactory.ResolveMarket(context, market);
        var locale = CustomerCatalogResponseFactory.ResolveLocale(context);
        var pageValue = page is null or < 1 ? 1 : page.Value;
        var pageSizeValue = pageSize is null or < 1 or > 100 ? 24 : pageSize.Value;

        var category = await dbContext.Categories
            .AsNoTracking()
            .Where(c => c.Slug == slug.Trim().ToLowerInvariant() && c.IsActive)
            .Select(c => new { c.Id, c.Slug })
            .SingleOrDefaultAsync(cancellationToken);

        if (category is null)
        {
            return CustomerCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status404NotFound,
                "catalog.category.not_found",
                "Category not found",
                "The requested category could not be found.");
        }

        var descendantIds = await dbContext.CategoryClosures
            .AsNoTracking()
            .Where(c => c.AncestorId == category.Id)
            .Select(c => c.DescendantId)
            .ToListAsync(cancellationToken);

        var productQuery = from p in dbContext.Products.AsNoTracking()
                           join pc in dbContext.ProductCategories.AsNoTracking()
                               on p.Id equals pc.ProductId
                           where descendantIds.Contains(pc.CategoryId)
                                 && p.Status == "published"
                                 && p.MarketCodes.Contains(marketCode)
                           select p;

        if (!string.IsNullOrWhiteSpace(brand))
        {
            var brandId = await dbContext.Brands
                .AsNoTracking()
                .Where(b => b.Slug == brand.Trim().ToLowerInvariant())
                .Select(b => (Guid?)b.Id)
                .SingleOrDefaultAsync(cancellationToken);

            if (brandId is Guid id)
            {
                productQuery = productQuery.Where(p => p.BrandId == id);
            }
            else
            {
                productQuery = productQuery.Where(p => false);
            }
        }

        if (priceMin is long min)
        {
            productQuery = productQuery.Where(p => p.PriceHintMinorUnits != null && p.PriceHintMinorUnits >= min);
        }

        if (priceMax is long max)
        {
            productQuery = productQuery.Where(p => p.PriceHintMinorUnits != null && p.PriceHintMinorUnits <= max);
        }

        if (string.Equals(restricted, "only-unrestricted", StringComparison.OrdinalIgnoreCase))
        {
            productQuery = productQuery.Where(p => !p.Restricted);
        }

        productQuery = ApplySort(productQuery, sort);

        var total = await productQuery.Distinct().CountAsync(cancellationToken);

        var items = await productQuery
            .Distinct()
            .Skip((pageValue - 1) * pageSizeValue)
            .Take(pageSizeValue)
            .Select(p => new ProductListItem(
                p.Id,
                p.Sku,
                locale == "ar" ? p.NameAr : p.NameEn,
                locale == "ar" ? p.SlugAr : p.SlugEn,
                p.NameAr,
                p.NameEn,
                p.BrandId,
                p.PriceHintMinorUnits,
                p.Restricted,
                p.RestrictionReasonCode))
            .ToListAsync(cancellationToken);

        var facets = await BuildFacetsAsync(dbContext, descendantIds, marketCode, cancellationToken);

        return Results.Ok(new GetCategoryProductsResponse(
            category.Slug,
            marketCode,
            pageValue,
            pageSizeValue,
            total,
            items,
            facets));
    }

    private static IQueryable<Entities.Product> ApplySort(IQueryable<Entities.Product> query, string? sort)
    {
        return sort?.Trim().ToLowerInvariant() switch
        {
            "price-asc" => query.OrderBy(p => p.PriceHintMinorUnits ?? long.MaxValue),
            "price-desc" => query.OrderByDescending(p => p.PriceHintMinorUnits ?? long.MinValue),
            "newest" => query.OrderByDescending(p => p.PublishedAt),
            _ => query.OrderByDescending(p => p.PublishedAt),
        };
    }

    private static async Task<ProductFacets> BuildFacetsAsync(
        CatalogDbContext dbContext,
        IReadOnlyCollection<Guid> categoryIds,
        string marketCode,
        CancellationToken cancellationToken)
    {
        var baseQuery = from p in dbContext.Products.AsNoTracking()
                        join pc in dbContext.ProductCategories.AsNoTracking() on p.Id equals pc.ProductId
                        where categoryIds.Contains(pc.CategoryId)
                              && p.Status == "published"
                              && p.MarketCodes.Contains(marketCode)
                        select p;

        var byBrand = await (
            from p in baseQuery.Distinct()
            join b in dbContext.Brands.AsNoTracking() on p.BrandId equals b.Id
            group p by new { b.Id, b.Slug, b.NameAr, b.NameEn } into g
            select new BrandFacetBucket(g.Key.Id, g.Key.Slug, g.Key.NameAr, g.Key.NameEn, g.Count()))
            .ToListAsync(cancellationToken);

        var rawPrices = await baseQuery.Distinct()
            .Where(p => p.PriceHintMinorUnits != null)
            .Select(p => p.PriceHintMinorUnits!.Value)
            .ToListAsync(cancellationToken);
        var priceBuckets = rawPrices
            .GroupBy(PriceBucket)
            .Select(g => new PriceFacetBucket(g.Key, g.Count()))
            .ToList();

        var restrictedCount = await baseQuery.Distinct().Where(p => p.Restricted).CountAsync(cancellationToken);
        var unrestrictedCount = await baseQuery.Distinct().Where(p => !p.Restricted).CountAsync(cancellationToken);

        return new ProductFacets(byBrand, priceBuckets, new RestrictionFacet(restrictedCount, unrestrictedCount));
    }

    private static string PriceBucket(long minorUnits)
    {
        var major = minorUnits / 100;
        return major switch
        {
            < 5000 => "0-5000",
            < 20000 => "5000-20000",
            < 100000 => "20000-100000",
            _ => "100000+",
        };
    }
}

public sealed record ProductListItem(
    Guid Id,
    string Sku,
    string LocalizedName,
    string LocalizedSlug,
    string NameAr,
    string NameEn,
    Guid BrandId,
    long? PriceHintMinorUnits,
    bool Restricted,
    string? RestrictionReasonCode);

public sealed record BrandFacetBucket(Guid Id, string Slug, string NameAr, string NameEn, int Count);

public sealed record PriceFacetBucket(string Range, int Count);

public sealed record RestrictionFacet(int Restricted, int Unrestricted);

public sealed record ProductFacets(
    IReadOnlyList<BrandFacetBucket> Brands,
    IReadOnlyList<PriceFacetBucket> PriceBuckets,
    RestrictionFacet Restriction);

public sealed record GetCategoryProductsResponse(
    string CategorySlug,
    string Market,
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<ProductListItem> Items,
    ProductFacets Facets);
