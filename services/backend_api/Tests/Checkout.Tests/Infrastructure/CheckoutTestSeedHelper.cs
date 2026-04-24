using BackendApi.Modules.Cart.Entities;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Inventory.Entities;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Caches;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Tests.Infrastructure;

public static class CheckoutTestSeedHelper
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

    public static async Task<Guid> EnsureWarehouseAsync(IServiceProvider services, string code, string marketCode, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<InventoryDbContext>();
        var existing = await db.Warehouses.SingleOrDefaultAsync(w => w.Code == code && w.MarketCode == marketCode, ct);
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
        try
        {
            await db.SaveChangesAsync(ct);
            return warehouse.Id;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var concurrent = await db.Warehouses.SingleOrDefaultAsync(w => w.Code == code && w.MarketCode == marketCode, ct);
            if (concurrent is null) throw;
            return concurrent.Id;
        }
    }

    public static async Task UpsertStockAsync(
        IServiceProvider services, Guid productId, Guid warehouseId, int onHand, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<InventoryDbContext>();
        var existing = await db.StockLevels.SingleOrDefaultAsync(x => x.ProductId == productId && x.WarehouseId == warehouseId, ct);
        if (existing is null)
        {
            db.StockLevels.Add(new StockLevel
            {
                ProductId = productId,
                WarehouseId = warehouseId,
                OnHand = onHand,
                Reserved = 0,
                SafetyStock = 0,
                ReorderThreshold = 0,
                BucketCache = "in_stock",
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.OnHand = onHand;
            existing.BucketCache = "in_stock";
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public static async Task AddBatchAsync(
        IServiceProvider services, Guid productId, Guid warehouseId, string lotNo, DateOnly expiryDate, int qtyOnHand, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<InventoryDbContext>();
        var marketCode = await db.Warehouses.Where(w => w.Id == warehouseId).Select(w => w.MarketCode).SingleAsync(ct);
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

    public static async Task EnsureTaxRateAsync(IServiceProvider services, string marketCode, int rateBps = 1500, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<PricingDbContext>();
        var existing = await db.TaxRates.AnyAsync(r => r.MarketCode == marketCode && r.Kind == "vat" && r.EffectiveTo == null, ct);
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
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                var nowExists = await db.TaxRates.AnyAsync(r => r.MarketCode == marketCode && r.Kind == "vat" && r.EffectiveTo == null, ct);
                if (!nowExists) throw;
            }
        }
        services.GetRequiredService<TaxRateCache>().InvalidateAll();
    }

    /// <summary>
    /// End-to-end seed for a happy-path checkout: publishes a product, seeds warehouse+stock+batch+tax,
    /// creates a cart for `accountId` with a line via the public cart surface semantics (entity writes),
    /// and returns the cart id. Reservation is booked via the Inventory orchestrator so SC-007
    /// invariants hold.
    /// </summary>
    public static async Task<Guid> SeedReadyCartAsync(
        IServiceProvider services,
        Guid accountId,
        string marketCode,
        Guid productId,
        int qty = 2,
        CancellationToken ct = default)
    {
        var cartDb = services.GetRequiredService<CartDbContext>();
        var inventoryDb = services.GetRequiredService<InventoryDbContext>();
        var catalogDb = services.GetRequiredService<CatalogDbContext>();
        var orchestrator = services.GetRequiredService<CartInventoryOrchestrator>();

        var cart = new BackendApi.Modules.Cart.Entities.Cart
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            MarketCode = marketCode,
            Status = CartStatuses.Active,
            LastTouchedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            OwnerId = "platform",
        };
        cartDb.Carts.Add(cart);
        await cartDb.SaveChangesAsync(ct);

        var reservation = await orchestrator.TryReserveAsync(
            inventoryDb, catalogDb, productId, qty, marketCode, accountId, cart.Id, DateTimeOffset.UtcNow, ct);
        var product = await catalogDb.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Restricted, p.RestrictionReasonCode })
            .SingleAsync(ct);
        cartDb.CartLines.Add(new CartLine
        {
            Id = Guid.NewGuid(),
            CartId = cart.Id,
            MarketCode = marketCode,
            ProductId = productId,
            Qty = qty,
            ReservationId = reservation.ReservationId,
            Restricted = product.Restricted,
            RestrictionReasonCode = product.RestrictionReasonCode,
            AddedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await cartDb.SaveChangesAsync(ct);
        return cart.Id;
    }
}
