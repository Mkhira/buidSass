using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Catalog.Primitives;
using BackendApi.Modules.Search.Primitives;
using BackendApi.Modules.Search.Primitives.Normalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Search.Tests.Infrastructure;

public static class SearchTestSeedHelper
{
    public static async Task<Guid> CreateCategoryAsync(
        IServiceProvider services,
        string slug,
        string? nameAr = null,
        string? nameEn = null,
        Guid? parentId = null,
        CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<CatalogDbContext>();
        var tree = services.GetRequiredService<CategoryTreeService>();

        var category = new Category
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            ParentId = parentId,
            NameAr = nameAr ?? slug,
            NameEn = nameEn ?? slug,
            IsActive = true,
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        await tree.InsertAsync(db, category.Id, parentId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return category.Id;
    }

    public static async Task<Guid> CreateBrandAsync(
        IServiceProvider services,
        string slug,
        string? nameAr = null,
        string? nameEn = null,
        CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<CatalogDbContext>();
        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            NameAr = nameAr ?? slug,
            NameEn = nameEn ?? slug,
            IsActive = true,
        };

        db.Brands.Add(brand);
        await db.SaveChangesAsync(cancellationToken);

        return brand.Id;
    }

    public static async Task<Guid> CreatePublishedProductAsync(
        IServiceProvider services,
        Guid brandId,
        IReadOnlyList<Guid> categoryIds,
        string sku,
        string nameAr,
        string nameEn,
        string[] marketCodes,
        bool restricted = false,
        string? restrictionReasonCode = null,
        string? barcode = null,
        long? priceHintMinorUnits = null,
        CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<CatalogDbContext>();
        var productId = Guid.NewGuid();

        var product = new Product
        {
            Id = productId,
            Sku = sku,
            Barcode = barcode,
            BrandId = brandId,
            SlugAr = $"slug-ar-{productId:N}",
            SlugEn = $"slug-en-{productId:N}",
            NameAr = nameAr,
            NameEn = nameEn,
            ShortDescriptionAr = nameAr,
            ShortDescriptionEn = nameEn,
            AttributesJson = "{}",
            MarketCodes = marketCodes,
            Status = "published",
            Restricted = restricted,
            RestrictionReasonCode = restrictionReasonCode,
            RestrictionMarkets = restricted ? marketCodes : Array.Empty<string>(),
            PriceHintMinorUnits = priceHintMinorUnits,
            PublishedAt = DateTimeOffset.UtcNow,
            CreatedByAccountId = Guid.NewGuid(),
        };

        db.Products.Add(product);

        foreach (var categoryId in categoryIds)
        {
            db.ProductCategories.Add(new ProductCategory
            {
                ProductId = productId,
                CategoryId = categoryId,
                IsPrimary = categoryId == categoryIds[0],
            });
        }

        db.ProductMedia.Add(new ProductMedia
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            StorageKey = $"catalog/{productId:N}/primary",
            ContentSha256 = new byte[32],
            MimeType = "image/jpeg",
            Bytes = 1024,
            WidthPx = 800,
            HeightPx = 800,
            IsPrimary = true,
            VariantStatus = "ready",
        });

        await db.SaveChangesAsync(cancellationToken);
        return productId;
    }

    public static async Task RebuildSearchIndexesAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var engine = services.GetRequiredService<ISearchEngine>();

        foreach (var index in IndexNames.All)
        {
            await engine.EnsureIndexAsync(index, cancellationToken);
            await engine.ClearIndexAsync(index.Name, cancellationToken);
        }

        await IndexAllPublishedProductsAsync(services, cancellationToken);
    }

    public static async Task IndexAllPublishedProductsAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<CatalogDbContext>();
        var engine = services.GetRequiredService<ISearchEngine>();
        var normalizer = services.GetRequiredService<ArabicNormalizer>();

        var products = await db.Products
            .AsNoTracking()
            .Where(p => p.Status == "published")
            .ToListAsync(cancellationToken);

        var brandIds = products.Select(x => x.BrandId).Distinct().ToArray();
        var brands = await db.Brands
            .AsNoTracking()
            .Where(b => brandIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        var productIds = products.Select(x => x.Id).ToArray();
        var categories = await (
            from pc in db.ProductCategories.AsNoTracking()
            join c in db.Categories.AsNoTracking() on pc.CategoryId equals c.Id
            where productIds.Contains(pc.ProductId)
            select new { pc.ProductId, CategoryId = c.Id, c.NameAr, c.NameEn })
            .ToListAsync(cancellationToken);

        var media = await db.ProductMedia
            .AsNoTracking()
            .Where(m => productIds.Contains(m.ProductId) && m.IsPrimary)
            .ToListAsync(cancellationToken);

        foreach (var product in products)
        {
            if (!brands.TryGetValue(product.BrandId, out var brand))
            {
                continue;
            }

            var productCategories = categories.Where(c => c.ProductId == product.Id).ToArray();
            var primaryMedia = media.FirstOrDefault(m => m.ProductId == product.Id);

            var snapshot = new CatalogProductSnapshot(
                product.Id,
                product.Sku,
                product.Barcode,
                product.NameAr,
                product.NameEn,
                product.ShortDescriptionAr,
                product.ShortDescriptionEn,
                product.BrandId,
                brand.NameAr,
                brand.NameEn,
                productCategories.Select(c => c.CategoryId).ToArray(),
                productCategories.Select(c => c.NameAr).ToArray(),
                productCategories.Select(c => c.NameEn).ToArray(),
                product.AttributesJson,
                product.PriceHintMinorUnits,
                product.Restricted,
                product.RestrictionReasonCode,
                product.MarketCodes,
                product.VendorId,
                product.PublishedAt,
                primaryMedia?.StorageKey is null ? null : $"/v1/customer/catalog/media/{primaryMedia.StorageKey}",
                primaryMedia?.StorageKey is null ? null : $"/v1/customer/catalog/media/{primaryMedia.StorageKey}");

            foreach (var market in product.MarketCodes.Select(m => m.Trim().ToLowerInvariant()))
            {
                if (!IndexNames.TryResolve(market, "ar", out var arIndex)
                    || !IndexNames.TryResolve(market, "en", out var enIndex))
                {
                    continue;
                }

                var arProjection = ProductSearchProjectionMapper.FromCatalogProduct(snapshot, "ar", market, normalizer);
                var enProjection = ProductSearchProjectionMapper.FromCatalogProduct(snapshot, "en", market, normalizer);

                await engine.UpsertAsync(arIndex.Name, [arProjection], cancellationToken);
                await engine.UpsertAsync(enIndex.Name, [enProjection], cancellationToken);
            }
        }
    }
}
