using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Inventory.Entities;
using BackendApi.Modules.Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Infrastructure;

public static class InventoryTestSeedHelper
{
    public static async Task<Guid> CreatePublishedProductAsync(
        IServiceProvider services,
        string sku,
        string[] marketCodes,
        CancellationToken ct = default)
    {
        var catalog = services.GetRequiredService<CatalogDbContext>();
        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            Slug = $"brand-{Guid.NewGuid():N}".Substring(0, 30),
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
            Restricted = false,
            RestrictionMarkets = Array.Empty<string>(),
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
        var existing = await db.Warehouses.SingleOrDefaultAsync(w => w.Code == code, ct);
        if (existing is not null)
        {
            return existing.Id;
        }

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
        int reorderThreshold = 0,
        string bucketCache = "out_of_stock",
        CancellationToken ct = default)
    {
        var db = services.GetRequiredService<InventoryDbContext>();
        var existing = await db.StockLevels.SingleOrDefaultAsync(
            x => x.ProductId == productId && x.WarehouseId == warehouseId,
            ct);

        if (existing is null)
        {
            db.StockLevels.Add(new StockLevel
            {
                ProductId = productId,
                WarehouseId = warehouseId,
                OnHand = onHand,
                Reserved = reserved,
                SafetyStock = safetyStock,
                ReorderThreshold = reorderThreshold,
                BucketCache = bucketCache,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.OnHand = onHand;
            existing.Reserved = reserved;
            existing.SafetyStock = safetyStock;
            existing.ReorderThreshold = reorderThreshold;
            existing.BucketCache = bucketCache;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public static async Task<Guid> AddBatchAsync(
        IServiceProvider services,
        Guid productId,
        Guid warehouseId,
        string lotNo,
        DateOnly expiryDate,
        int qtyOnHand,
        CancellationToken ct = default)
    {
        var db = services.GetRequiredService<InventoryDbContext>();
        var batch = new InventoryBatch
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            WarehouseId = warehouseId,
            LotNo = lotNo,
            ExpiryDate = expiryDate,
            QtyOnHand = qtyOnHand,
            Status = "active",
            ReceivedAt = DateTimeOffset.UtcNow,
        };

        db.InventoryBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        return batch.Id;
    }
}
