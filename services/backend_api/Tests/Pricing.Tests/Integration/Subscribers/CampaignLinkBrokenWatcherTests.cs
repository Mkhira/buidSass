using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using BackendApi.Modules.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Subscribers;

/// <summary>
/// Spec 007-b T134 — verifies <c>CampaignLinkBrokenWatcher</c> auto-marks
/// referencing campaigns + their campaign_links when the linked promotion or
/// coupon is deactivated or expires (FR-019). The Campaign itself does NOT
/// auto-deactivate — only the broken-link indicator flips, plus a
/// <c>campaign.updated</c> audit row is staged (CodeRabbit PR #81 round 1).
/// </summary>
[Collection("pricing-fixture")]
public sealed class CampaignLinkBrokenWatcherTests(PricingTestFactory factory)
{
    [Fact]
    public async Task PromotionDeactivated_MarksReferencingCampaignAsLinkBroken()
    {
        await factory.ResetDatabaseAsync();
        var promoId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            db.Campaigns.Add(BuildCampaign(campaignId));
            db.CampaignLinks.Add(new CampaignLink
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                Kind = "promotion",
                TargetId = promoId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
            await publisher.Publish(
                new PromotionDeactivated(
                    promoId, DateTimeOffset.UtcNow,
                    CommercialPermissions.SystemActorId, "operator paused", 1800),
                CancellationToken.None);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            var c = await db.Campaigns.AsNoTracking().SingleAsync(c => c.Id == campaignId);
            c.LinkBroken.Should().BeTrue();
            c.State.Should().Be(LifecycleState.Draft, "campaigns must NOT auto-deactivate (FR-019)");

            var link = await db.CampaignLinks.AsNoTracking()
                .SingleAsync(l => l.CampaignId == campaignId);
            link.LinkBrokenAtUtc.Should().NotBeNull();

            // Audit row for the broken-link flip (CodeRabbit PR #81 round 1 Major).
            var auditRows = await db.CommercialAuditEvents.AsNoTracking()
                .Where(e => e.TargetEntityId == campaignId &&
                            e.Kind == "campaign.updated")
                .ToListAsync();
            auditRows.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task CouponExpired_MarksReferencingCampaignAsLinkBroken()
    {
        await factory.ResetDatabaseAsync();
        var couponId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            db.Campaigns.Add(BuildCampaign(campaignId));
            db.CampaignLinks.Add(new CampaignLink
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                Kind = "coupon",
                TargetId = couponId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
            await publisher.Publish(
                new CouponExpired(couponId, DateTimeOffset.UtcNow),
                CancellationToken.None);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            var c = await db.Campaigns.AsNoTracking().SingleAsync(c => c.Id == campaignId);
            c.LinkBroken.Should().BeTrue();
        }
    }

    [Fact]
    public async Task RedeliveryAgainstAlreadyBrokenCampaign_IsIdempotent()
    {
        await factory.ResetDatabaseAsync();
        var promoId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            var c = BuildCampaign(campaignId);
            c.LinkBroken = true; // already broken
            db.Campaigns.Add(c);
            db.CampaignLinks.Add(new CampaignLink
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                Kind = "promotion",
                TargetId = promoId,
                LinkBrokenAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            });
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
            await publisher.Publish(
                new PromotionDeactivated(
                    promoId, DateTimeOffset.UtcNow,
                    CommercialPermissions.SystemActorId, "redelivery", 1800),
                CancellationToken.None);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            var auditRows = await db.CommercialAuditEvents.AsNoTracking()
                .Where(e => e.TargetEntityId == campaignId &&
                            e.Kind == "campaign.updated")
                .CountAsync();
            auditRows.Should().Be(0,
                "redelivery against an already-broken campaign writes NO duplicate audit row");
        }
    }

    private static Campaign BuildCampaign(Guid id) => new()
    {
        Id = id,
        NameAr = "حملة اختبارية",
        NameEn = "Watcher test campaign",
        ValidFrom = DateTimeOffset.UtcNow.AddDays(1),
        ValidTo = DateTimeOffset.UtcNow.AddDays(30),
        Markets = new[] { "sa" },
        State = LifecycleState.Draft,
        StateChangedAtUtc = DateTimeOffset.UtcNow,
        StateChangedByActorId = CommercialPermissions.SystemActorId,
        LinkBroken = false,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
