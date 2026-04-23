using BackendApi.Modules.Catalog.Customer.Common;
using BackendApi.Modules.Catalog.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Catalog.Customer.ListBrands;

public static class ListBrandsEndpoint
{
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/brands", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        string? market,
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var marketCode = CustomerCatalogResponseFactory.ResolveMarket(context, market);

        var brandsWithCounts = await (
            from b in dbContext.Brands.AsNoTracking()
            where b.IsActive
            let productCount = dbContext.Products
                .Where(p => p.BrandId == b.Id
                            && p.Status == "published"
                            && p.MarketCodes.Contains(marketCode))
                .Count()
            orderby b.NameEn
            select new BrandSummary(b.Id, b.Slug, b.NameAr, b.NameEn, productCount))
            .ToListAsync(cancellationToken);

        return Results.Ok(new ListBrandsResponse(brandsWithCounts, marketCode));
    }
}

public sealed record BrandSummary(Guid Id, string Slug, string NameAr, string NameEn, int ProductCount);

public sealed record ListBrandsResponse(IReadOnlyList<BrandSummary> Brands, string Market);
