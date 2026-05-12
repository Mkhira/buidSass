using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Subscribers;

/// <summary>
/// Spec 007-b T133 — verifies the B2BCompanySuspended subscriber flips
/// <c>company_link_broken=true</c> on every active company-override row owned
/// by the suspended company; idempotent re-delivery is a no-op (data-model
/// §3.2; research §R3).
/// </summary>
[Collection("pricing-fixture")]
public sealed class B2BCompanySuspendedHandlerTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Handle_FlipsCompanyLinkBrokenOnReferencingOverrideRows()
    {
        await factory.ResetDatabaseAsync();
        var companyId = Guid.NewGuid();
        var rowA = Guid.NewGuid();
        var rowB = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            db.ProductTierPrices.Add(BuildCompanyOverride(rowA, companyId, "sa"));
            db.ProductTierPrices.Add(BuildCompanyOverride(rowB, companyId, "eg"));
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IB2BCompanySuspendedSubscriber>();
            await handler.HandleAsync(
                new B2BCompanySuspendedEvent(
                    companyId, DateTimeOffset.UtcNow,
                    CommercialPermissions.SystemActorId, "policy violation"),
                CancellationToken.None);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            var rows = await db.ProductTierPrices.AsNoTracking()
                .Where(r => r.CompanyId == companyId)
                .ToListAsync();
            rows.Should().HaveCount(2);
            rows.Should().OnlyContain(r => r.CompanyLinkBroken);
            rows.Should().OnlyContain(r => r.CompanyLinkBrokenAtUtc != null);
        }
    }

    [Fact]
    public async Task Handle_IsNoOp_WhenNoReferencingRows()
    {
        await factory.ResetDatabaseAsync();
        var handler = factory.Services.GetRequiredService<IB2BCompanySuspendedSubscriber>();

        var act = async () => await handler.HandleAsync(
            new B2BCompanySuspendedEvent(
                Guid.NewGuid(), DateTimeOffset.UtcNow,
                CommercialPermissions.SystemActorId, "no rows reference this"),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_SkipsAlreadyBrokenRows_Idempotent()
    {
        await factory.ResetDatabaseAsync();
        var companyId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var firstBrokenAt = DateTimeOffset.UtcNow.AddHours(-2);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            var row = BuildCompanyOverride(rowId, companyId, "sa");
            row.CompanyLinkBroken = true;
            row.CompanyLinkBrokenAtUtc = firstBrokenAt;
            db.ProductTierPrices.Add(row);
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IB2BCompanySuspendedSubscriber>();
            await handler.HandleAsync(
                new B2BCompanySuspendedEvent(
                    companyId, DateTimeOffset.UtcNow,
                    CommercialPermissions.SystemActorId, "re-delivery"),
                CancellationToken.None);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            var row = await db.ProductTierPrices.AsNoTracking().SingleAsync(r => r.Id == rowId);
            row.CompanyLinkBroken.Should().BeTrue();
            row.CompanyLinkBrokenAtUtc.Should().BeCloseTo(firstBrokenAt, TimeSpan.FromSeconds(1),
                "the original timestamp must not be clobbered by redelivery");
        }
    }

    private static ProductTierPrice BuildCompanyOverride(Guid id, Guid companyId, string market) => new()
    {
        Id = id,
        ProductId = Guid.NewGuid(),
        TierId = null,
        CompanyId = companyId,
        MarketCode = market,
        NetMinor = 1_00_000,
        State = BusinessPricingState.Active,
        StateChangedAtUtc = DateTimeOffset.UtcNow,
        StateChangedByActorId = CommercialPermissions.SystemActorId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
