using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Inventory.Entities;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Caches;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Infrastructure;

public static class CartTestSeedHelper
{
    public static async Task<Guid> CreatePublishedProductAsync(
        IServiceProvider services,
        string sku,
        string[] marketCodes,
        bool restricted = false,
        string? restrictionReasonCode = null,
        long priceHintMinor = 10_000,
        CancellationToken ct = default)
    {
        var catalog = services.GetRequiredService<CatalogDbContext>();

        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            Slug = $"b-{Guid.NewGuid():N}"[..20],
            NameAr = "علامة",
            NameEn = "Brand",
            IsActive = true,
        };
        catalog.Brands.Add(brand);

        var productId = Guid.NewGuid();
        catalog.Products.Add(new Product
        {
            Id = productId,
            Sku = sku,
            BrandId = brand.Id,
            SlugAr = $"slug-ar-{productId:N}",
            SlugEn = $"slug-en-{productId:N}",
            NameAr = "منتج",
            NameEn = "Product",
            AttributesJson = "{}",
            MarketCodes = marketCodes,
            Status = "published",
            Restricted = restricted,
            RestrictionReasonCode = restrictionReasonCode,
            RestrictionMarkets = restricted ? marketCodes : Array.Empty<string>(),
            PriceHintMinorUnits = priceHintMinor,
            PublishedAt = DateTimeOffset.UtcNow,
            CreatedByAccountId = Guid.NewGuid(),
        });

        await catalog.SaveChangesAsync(ct);
        return productId;
    }

    public static async Task<Guid> EnsureWarehouseAsync(
        IServiceProvider services,
        string code,
        string marketCode,
        CancellationToken ct = default)
    {
        var db = services.GetRequiredService<InventoryDbContext>();
        var existing = await db.Warehouses.SingleOrDefaultAsync(
            w => w.Code == code && w.MarketCode == marketCode, ct);
        if (existing is not null) return existing.Id;

        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            Code = code,
            MarketCode = marketCode,
            DisplayName = code,
            IsActive = true,
        };
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync(ct);
        return warehouse.Id;
    }

    public static async Task UpsertStockAsync(
        IServiceProvider services,
        Guid productId,
        Guid warehouseId,
        int onHand,
        int reserved = 0,
        int safetyStock = 0,
        string bucketCache = "in_stock",
        CancellationToken ct = default)
    {
        var db = services.GetRequiredService<InventoryDbContext>();
        var existing = await db.StockLevels.SingleOrDefaultAsync(
            x => x.ProductId == productId && x.WarehouseId == warehouseId, ct);

        if (existing is null)
        {
            db.StockLevels.Add(new StockLevel
            {
                ProductId = productId,
                WarehouseId = warehouseId,
                OnHand = onHand,
                Reserved = reserved,
                SafetyStock = safetyStock,
                ReorderThreshold = 0,
                BucketCache = bucketCache,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.OnHand = onHand;
            existing.Reserved = reserved;
            existing.SafetyStock = safetyStock;
            existing.BucketCache = bucketCache;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public static async Task AddBatchAsync(
        IServiceProvider services,
        Guid productId,
        Guid warehouseId,
        string lotNo,
        DateOnly expiryDate,
        int qtyOnHand,
        CancellationToken ct = default)
    {
        var db = services.GetRequiredService<InventoryDbContext>();
        var marketCode = await db.Warehouses
            .Where(w => w.Id == warehouseId)
            .Select(w => w.MarketCode)
            .SingleAsync(ct);

        db.InventoryBatches.Add(new InventoryBatch
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            WarehouseId = warehouseId,
            MarketCode = marketCode,
            LotNo = lotNo,
            ExpiryDate = expiryDate,
            QtyOnHand = qtyOnHand,
            Status = "active",
            ReceivedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Seeds a VAT rate for the market so pricing TaxLayer resolves tax without error.</summary>
    public static async Task EnsureTaxRateAsync(
        IServiceProvider services,
        string marketCode,
        int rateBps = 1500,
        CancellationToken ct = default)
    {
        var db = services.GetRequiredService<PricingDbContext>();
        var existing = await db.TaxRates.AnyAsync(r => r.MarketCode == marketCode && r.EffectiveTo == null, ct);
        if (!existing)
        {
            db.TaxRates.Add(new TaxRate
            {
                Id = Guid.NewGuid(),
                MarketCode = marketCode,
                Kind = "vat",
                RateBps = rateBps,
                EffectiveFrom = DateTimeOffset.UtcNow.AddYears(-1),
                EffectiveTo = null,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
        services.GetRequiredService<TaxRateCache>().InvalidateAll();
    }
}
