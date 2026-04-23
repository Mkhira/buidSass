using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Catalog.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Tests.Infrastructure;

/// <summary>
/// Shared seed helpers for contract and integration tests. Every helper runs inside the caller's
/// scope and never modifies the fixture; callers typically invoke <see cref="ResetAsync"/> first.
/// </summary>
public static class CatalogTestSeedHelper
{
    public static async Task<Guid> CreateCategoryAsync(
        IServiceProvider services,
        string slug,
        Guid? parentId = null,
        string? nameAr = null,
        string? nameEn = null,
        bool isActive = true,
        CancellationToken cancellationToken = default)
    {
        var dbContext = services.GetRequiredService<CatalogDbContext>();
        var tree = services.GetRequiredService<CategoryTreeService>();

        var category = new Category
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            ParentId = parentId,
            NameAr = nameAr ?? slug,
            NameEn = nameEn ?? slug,
            IsActive = isActive,
        };
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);
        await tree.InsertAsync(dbContext, category.Id, parentId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return category.Id;
    }

    public static async Task<Guid> CreateBrandAsync(
        IServiceProvider services,
        string slug,
        string? nameEn = null,
        CancellationToken cancellationToken = default)
    {
        var dbContext = services.GetRequiredService<CatalogDbContext>();
        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            NameAr = slug,
            NameEn = nameEn ?? slug,
        };
        dbContext.Brands.Add(brand);
        await dbContext.SaveChangesAsync(cancellationToken);
        return brand.Id;
    }

    public static async Task<Guid> CreateProductAsync(
        IServiceProvider services,
        Guid brandId,
        Guid? primaryCategoryId = null,
        string? sku = null,
        string status = "published",
        string[]? marketCodes = null,
        bool restricted = false,
        string? restrictionReasonCode = null,
        string[]? restrictionMarkets = null,
        long? priceHintMinorUnits = null,
        bool hasPrimaryMedia = true,
        CancellationToken cancellationToken = default)
    {
        var dbContext = services.GetRequiredService<CatalogDbContext>();
        var productId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var product = new Product
        {
            Id = productId,
            Sku = sku ?? $"sku-{productId:N}",
            BrandId = brandId,
            SlugAr = $"ar-{productId:N}",
            SlugEn = $"en-{productId:N}",
            NameAr = "منتج",
            NameEn = "Product",
            MarketCodes = marketCodes ?? new[] { "ksa" },
            Status = status,
            Restricted = restricted,
            RestrictionReasonCode = restrictionReasonCode,
            RestrictionMarkets = restrictionMarkets ?? Array.Empty<string>(),
            PriceHintMinorUnits = priceHintMinorUnits,
            PublishedAt = status == "published" ? now : null,
            CreatedByAccountId = Guid.NewGuid(),
        };
        dbContext.Products.Add(product);

        if (primaryCategoryId is Guid catId)
        {
            dbContext.ProductCategories.Add(new ProductCategory
            {
                ProductId = productId,
                CategoryId = catId,
                IsPrimary = true,
            });
        }

        if (hasPrimaryMedia)
        {
            dbContext.ProductMedia.Add(new ProductMedia
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
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return productId;
    }
}
