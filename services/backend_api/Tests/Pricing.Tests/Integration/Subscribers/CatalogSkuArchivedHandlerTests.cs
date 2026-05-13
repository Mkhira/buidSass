using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using BackendApi.Modules.Pricing.Subscribers;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Subscribers;

/// <summary>
/// Spec 007-b T132 — verifies <see cref="CatalogSkuArchivedHandler"/>
/// flips <c>applies_to_broken</c> on referencing Promotions and
/// <c>company_link_broken</c> on referencing tier rows (data-model §3, §7;
/// research §R3). Idempotency: re-delivery against an already-marked row is a
/// no-op.
/// </summary>
[Collection("pricing-fixture")]
public sealed class CatalogSkuArchivedHandlerTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Handle_FlipsAppliesToBrokenOnReferencingPromotion()
    {
        await factory.ResetDatabaseAsync();
        var skuId = Guid.NewGuid();
        var promoId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            db.Promotions.Add(BuildPromotion(promoId, skuId));
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<ICatalogSkuArchivedSubscriber>();
            await handler.HandleAsync(
                new CatalogSkuArchivedEvent(
                    skuId, "SKU-XYZ", DateTimeOffset.UtcNow, CommercialPermissions.SystemActorId),
                CancellationToken.None);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            var promo = await db.Promotions.AsNoTracking().SingleAsync(p => p.Id == promoId);
            promo.AppliesToBroken.Should().BeTrue();
            promo.AppliesToBrokenAtUtc.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Handle_FlipsCompanyLinkBrokenOnReferencingTierRow()
    {
        await factory.ResetDatabaseAsync();
        var skuId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var tierId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            db.ProductTierPrices.Add(new ProductTierPrice
            {
                Id = rowId,
                ProductId = skuId,
                TierId = tierId,
                MarketCode = "sa",
                NetMinor = 5_00_000,
                State = BusinessPricingState.Active,
                StateChangedAtUtc = DateTimeOffset.UtcNow,
                StateChangedByActorId = CommercialPermissions.SystemActorId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<ICatalogSkuArchivedSubscriber>();
            await handler.HandleAsync(
                new CatalogSkuArchivedEvent(
                    skuId, "SKU-ARC", DateTimeOffset.UtcNow, CommercialPermissions.SystemActorId),
                CancellationToken.None);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            var row = await db.ProductTierPrices.AsNoTracking().SingleAsync(r => r.Id == rowId);
            row.CompanyLinkBroken.Should().BeTrue();
            row.CompanyLinkBrokenAtUtc.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Handle_IsIdempotentOnAlreadyMarkedRows()
    {
        await factory.ResetDatabaseAsync();
        var skuId = Guid.NewGuid();
        var promoId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            var p = BuildPromotion(promoId, skuId);
            p.AppliesToBroken = true;
            p.AppliesToBrokenAtUtc = DateTimeOffset.UtcNow.AddHours(-1);
            db.Promotions.Add(p);
            await db.SaveChangesAsync();
        }

        // CodeRabbit PR #82 round 1: resolve from a scope to match the
        // lifetime pattern used by the other tests in this file.
        await using (var scopeAct = factory.Services.CreateAsyncScope())
        {
            var handler = scopeAct.ServiceProvider.GetRequiredService<ICatalogSkuArchivedSubscriber>();
            var act = async () => await handler.HandleAsync(
                new CatalogSkuArchivedEvent(
                    skuId, "SKU-X", DateTimeOffset.UtcNow, CommercialPermissions.SystemActorId),
                CancellationToken.None);
            await act.Should().NotThrowAsync("redelivery against already-flagged rows must be a no-op");
        }

        await using var scope2 = factory.Services.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<PricingDbContext>();
        var promo = await db2.Promotions.AsNoTracking().SingleAsync(p => p.Id == promoId);
        promo.AppliesToBroken.Should().BeTrue();
    }

    private static Promotion BuildPromotion(Guid id, Guid productId) => new()
    {
        Id = id,
        Kind = "percent_off",
        PercentOff = 10,
        AppliesToProductIds = new[] { productId },
        MarketCodes = new[] { "sa" },
        Priority = 100,
        StacksWithCoupons = true,
        StacksWithOtherPromotions = true,
        State = LifecycleState.Active,
        StateChangedAtUtc = DateTimeOffset.UtcNow,
        StateChangedByActorId = CommercialPermissions.SystemActorId,
        AppliesToBroken = false,
        LabelAr = "اختبار",
        LabelEn = "Subscriber test promo",
        Name = "Subscriber test promo",
        StartsAt = DateTimeOffset.UtcNow.AddDays(-1),
        EndsAt = DateTimeOffset.UtcNow.AddDays(7),
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
