using BackendApi.Modules.Catalog.Customer.Common;
using BackendApi.Modules.Catalog.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Catalog.Customer.GetProductBySlug;

public static class GetProductBySlugEndpoint
{
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/products/{slug}", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        string slug,
        string? market,
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var marketCode = CustomerCatalogResponseFactory.ResolveMarket(context, market);
        var locale = CustomerCatalogResponseFactory.ResolveLocale(context);
        var trimmed = slug.Trim().ToLowerInvariant();

        var product = await dbContext.Products
            .AsNoTracking()
            .Where(p => (p.SlugAr == trimmed || p.SlugEn == trimmed)
                        && p.Status == "published"
                        && p.MarketCodes.Contains(marketCode))
            .Select(p => new ProductDetailProjection(
                p.Id,
                p.Sku,
                p.Barcode,
                p.BrandId,
                p.ManufacturerId,
                p.SlugAr,
                p.SlugEn,
                p.NameAr,
                p.NameEn,
                p.ShortDescriptionAr,
                p.ShortDescriptionEn,
                p.DescriptionAr,
                p.DescriptionEn,
                p.AttributesJson,
                p.MarketCodes,
                p.PriceHintMinorUnits,
                p.Restricted,
                p.RestrictionReasonCode,
                p.PublishedAt))
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return CustomerCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status404NotFound,
                "catalog.product.not_found",
                "Product not found",
                "The requested product could not be found.");
        }

        var localeFallbacks = new List<LocaleFallback>();
        var localizedName = PickLocalized(locale, product.NameAr, product.NameEn, nameof(ProductDetailProjection.NameAr), nameof(ProductDetailProjection.NameEn), localeFallbacks, "name");
        var localizedShortDescription = PickLocalizedNullable(locale, product.ShortDescriptionAr, product.ShortDescriptionEn, localeFallbacks, "shortDescription");
        var localizedDescription = PickLocalizedNullable(locale, product.DescriptionAr, product.DescriptionEn, localeFallbacks, "description");

        var categories = await (
            from pc in dbContext.ProductCategories.AsNoTracking()
            join c in dbContext.Categories.AsNoTracking() on pc.CategoryId equals c.Id
            where pc.ProductId == product.Id
            orderby pc.IsPrimary descending, c.NameEn
            select new ProductCategoryBreadcrumb(c.Id, c.Slug, c.NameAr, c.NameEn, pc.IsPrimary))
            .ToListAsync(cancellationToken);

        var media = await dbContext.ProductMedia
            .AsNoTracking()
            .Where(m => m.ProductId == product.Id)
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.DisplayOrder)
            .Select(m => new ProductMediaSummary(m.Id, m.StorageKey, m.AltAr, m.AltEn, m.IsPrimary, m.VariantStatus, m.VariantsJson))
            .ToListAsync(cancellationToken);

        var documents = await dbContext.ProductDocuments
            .AsNoTracking()
            .Where(d => d.ProductId == product.Id && d.Locale == locale)
            .Select(d => new ProductDocumentSummary(d.Id, d.DocType, d.Locale, d.TitleAr, d.TitleEn, d.StorageKey))
            .ToListAsync(cancellationToken);

        if (localeFallbacks.Count > 0)
        {
            context.Response.Headers["x-locale-fallback"] = string.Join(",", localeFallbacks.Select(f => $"{f.Field}:{f.OriginalLocale}"));
        }

        return Results.Ok(new GetProductBySlugResponse(
            product.Id,
            product.Sku,
            product.Barcode,
            product.BrandId,
            product.ManufacturerId,
            localizedName,
            product.SlugAr,
            product.SlugEn,
            localizedShortDescription,
            localizedDescription,
            product.AttributesJson,
            product.MarketCodes,
            product.PriceHintMinorUnits,
            new RestrictionBadge(product.Restricted, product.RestrictionReasonCode),
            categories,
            media,
            documents,
            product.PublishedAt));
    }

    private static string PickLocalized(
        string locale,
        string ar,
        string en,
        string arField,
        string enField,
        List<LocaleFallback> fallbacks,
        string fieldLabel)
    {
        if (locale == "ar" && !string.IsNullOrWhiteSpace(ar))
        {
            return ar;
        }

        if (locale == "en" && !string.IsNullOrWhiteSpace(en))
        {
            return en;
        }

        if (locale == "ar")
        {
            fallbacks.Add(new LocaleFallback(fieldLabel, "en"));
            return en;
        }

        fallbacks.Add(new LocaleFallback(fieldLabel, "ar"));
        return ar;
    }

    private static string? PickLocalizedNullable(
        string locale,
        string? ar,
        string? en,
        List<LocaleFallback> fallbacks,
        string fieldLabel)
    {
        if (locale == "ar" && !string.IsNullOrWhiteSpace(ar))
        {
            return ar;
        }

        if (locale == "en" && !string.IsNullOrWhiteSpace(en))
        {
            return en;
        }

        if (locale == "ar" && !string.IsNullOrWhiteSpace(en))
        {
            fallbacks.Add(new LocaleFallback(fieldLabel, "en"));
            return en;
        }

        if (locale == "en" && !string.IsNullOrWhiteSpace(ar))
        {
            fallbacks.Add(new LocaleFallback(fieldLabel, "ar"));
            return ar;
        }

        return null;
    }
}

internal sealed record ProductDetailProjection(
    Guid Id,
    string Sku,
    string? Barcode,
    Guid BrandId,
    Guid? ManufacturerId,
    string SlugAr,
    string SlugEn,
    string NameAr,
    string NameEn,
    string? ShortDescriptionAr,
    string? ShortDescriptionEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string AttributesJson,
    string[] MarketCodes,
    long? PriceHintMinorUnits,
    bool Restricted,
    string? RestrictionReasonCode,
    DateTimeOffset? PublishedAt);

internal sealed record LocaleFallback(string Field, string OriginalLocale);

public sealed record ProductCategoryBreadcrumb(Guid Id, string Slug, string NameAr, string NameEn, bool IsPrimary);

public sealed record ProductMediaSummary(Guid Id, string StorageKey, string? AltAr, string? AltEn, bool IsPrimary, string VariantStatus, string VariantsJson);

public sealed record ProductDocumentSummary(Guid Id, string DocType, string Locale, string? TitleAr, string? TitleEn, string StorageKey);

public sealed record RestrictionBadge(bool Restricted, string? ReasonCode);

public sealed record GetProductBySlugResponse(
    Guid Id,
    string Sku,
    string? Barcode,
    Guid BrandId,
    Guid? ManufacturerId,
    string LocalizedName,
    string SlugAr,
    string SlugEn,
    string? LocalizedShortDescription,
    string? LocalizedDescription,
    string AttributesJson,
    string[] MarketCodes,
    long? PriceHintMinorUnits,
    RestrictionBadge Restriction,
    IReadOnlyList<ProductCategoryBreadcrumb> Categories,
    IReadOnlyList<ProductMediaSummary> Media,
    IReadOnlyList<ProductDocumentSummary> Documents,
    DateTimeOffset? PublishedAt);
