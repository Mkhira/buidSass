using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Pricing.Tests.Infrastructure;

public static class PricingTestSeedHelper
{
    public static async Task SeedKsaVatAsync(IServiceProvider services, int rateBps = 1_500, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<PricingDbContext>();
        db.TaxRates.Add(new TaxRate
        {
            Id = Guid.NewGuid(),
            MarketCode = "ksa",
            Kind = "vat",
            RateBps = rateBps,
            EffectiveFrom = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo = null,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public static async Task SeedEgVatAsync(IServiceProvider services, int rateBps = 1_400, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<PricingDbContext>();
        db.TaxRates.Add(new TaxRate
        {
            Id = Guid.NewGuid(),
            MarketCode = "eg",
            Kind = "vat",
            RateBps = rateBps,
            EffectiveFrom = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo = null,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public static async Task<Guid> CreatePublishedProductAsync(
        IServiceProvider services,
        string sku,
        long priceHintMinor,
        string[] marketCodes,
        bool restricted = false,
        string? restrictionReasonCode = null,
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
        var product = new Product
        {
            Id = productId,
            Sku = sku,
            BrandId = brand.Id,
            SlugAr = $"slug-ar-{productId:N}",
            SlugEn = $"slug-en-{productId:N}",
            NameAr = "منتج",
            NameEn = "Product",
            ShortDescriptionAr = "وصف",
            ShortDescriptionEn = "Desc",
            AttributesJson = "{}",
            MarketCodes = marketCodes,
            Status = "published",
            Restricted = restricted,
            RestrictionReasonCode = restrictionReasonCode,
            RestrictionMarkets = restricted ? marketCodes : Array.Empty<string>(),
            PriceHintMinorUnits = priceHintMinor,
            PublishedAt = DateTimeOffset.UtcNow,
            CreatedByAccountId = Guid.NewGuid(),
        };
        catalog.Products.Add(product);
        await catalog.SaveChangesAsync(ct);
        return productId;
    }

    public static async Task<Guid> CreateTierAsync(IServiceProvider services, string slug, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<PricingDbContext>();
        var tier = new B2BTier
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Name = slug,
            DefaultDiscountBps = 0,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.B2BTiers.Add(tier);
        await db.SaveChangesAsync(ct);
        return tier.Id;
    }

    public static async Task AssignTierAsync(IServiceProvider services, Guid accountId, Guid tierId, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<PricingDbContext>();
        db.AccountB2BTiers.Add(new AccountB2BTier
        {
            AccountId = accountId,
            TierId = tierId,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByAccountId = Guid.Empty,
        });
        await db.SaveChangesAsync(ct);
    }

    public static async Task UpsertTierPriceAsync(IServiceProvider services, Guid productId, Guid tierId, string marketCode, long netMinor, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<PricingDbContext>();
        db.ProductTierPrices.Add(new ProductTierPrice
        {
            ProductId = productId,
            TierId = tierId,
            MarketCode = marketCode,
            NetMinor = netMinor,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public static async Task<Guid> CreateCouponAsync(
        IServiceProvider services,
        string code,
        string kind = "percent",
        int value = 1_000,
        long? capMinor = null,
        int? perCustomerLimit = null,
        int? overallLimit = null,
        bool excludesRestricted = false,
        string[]? markets = null,
        DateTimeOffset? validTo = null,
        CancellationToken ct = default)
    {
        var db = services.GetRequiredService<PricingDbContext>();
        var coupon = new Coupon
        {
            Id = Guid.NewGuid(),
            Code = code.ToUpperInvariant(),
            Kind = kind,
            Value = value,
            CapMinor = capMinor,
            PerCustomerLimit = perCustomerLimit,
            OverallLimit = overallLimit,
            ExcludesRestricted = excludesRestricted,
            MarketCodes = markets ?? new[] { "ksa" },
            ValidFrom = null,
            ValidTo = validTo,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Coupons.Add(coupon);
        await db.SaveChangesAsync(ct);
        return coupon.Id;
    }

    public static async Task<Guid> CreatePromotionAsync(
        IServiceProvider services,
        string kind,
        object config,
        string[]? markets = null,
        int priority = 0,
        CancellationToken ct = default)
    {
        var db = services.GetRequiredService<PricingDbContext>();
        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Name = kind,
            ConfigJson = System.Text.Json.JsonSerializer.Serialize(config),
            MarketCodes = markets ?? new[] { "ksa" },
            Priority = priority,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Promotions.Add(promo);
        await db.SaveChangesAsync(ct);
        return promo.Id;
    }
}
