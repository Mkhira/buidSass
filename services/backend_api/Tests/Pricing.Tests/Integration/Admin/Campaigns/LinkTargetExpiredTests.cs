using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Pricing.Admin.Campaigns;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Admin.Campaigns;

/// <summary>
/// Spec 007-b T094 — picking an Expired promotion as a campaign link target
/// returns 400 with reason <c>campaign.link.target_expired</c>
/// (US4 acceptance scenario 2).
/// </summary>
[Collection("pricing-fixture")]
public sealed class LinkTargetExpiredTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Create_LinkingExpiredPromotion_Returns400_TargetExpired()
    {
        await factory.ResetDatabaseAsync();
        var client = await BuildOperatorClientAsync();
        var expiredPromoId = await SeedExpiredPromotionAsync();

        var resp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/campaigns",
            BuildCampaignRequest(linkKind: "promotion", linkTargetId: expiredPromoId));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync())
            .Should().Contain(CommercialReasonCode.CampaignLinkTargetExpired);
    }

    [Fact]
    public async Task Create_LinkingMissingPromotion_Returns400_TargetNotFound()
    {
        await factory.ResetDatabaseAsync();
        var client = await BuildOperatorClientAsync();

        var resp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/campaigns",
            BuildCampaignRequest(linkKind: "promotion", linkTargetId: Guid.NewGuid()));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync())
            .Should().Contain(CommercialReasonCode.CampaignLinkTargetNotFound);
    }

    private async Task<HttpClient> BuildOperatorClientAsync()
    {
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);
        return client;
    }

    private async Task<Guid> SeedExpiredPromotionAsync()
    {
        var id = Guid.NewGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        db.Promotions.Add(new Promotion
        {
            Id = id,
            Kind = "percent_off",
            PercentOff = 10,
            MarketCodes = new[] { "sa" },
            Priority = 100,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-60),
            EndsAt = DateTimeOffset.UtcNow.AddDays(-30),
            StacksWithCoupons = true,
            StacksWithOtherPromotions = true,
            State = LifecycleState.Expired,
            StateChangedAtUtc = DateTimeOffset.UtcNow.AddDays(-30),
            StateChangedByActorId = CommercialPermissions.SystemActorId,
            LabelAr = "منتهي",
            LabelEn = "Expired",
            Name = "Expired Promo",
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-60),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static CreateCampaignRequest BuildCampaignRequest(string linkKind, Guid? linkTargetId) => new(
        Name: new CampaignBilingual("حملة الاختبار", "Link-target test"),
        ValidFrom: DateTimeOffset.UtcNow.AddDays(1),
        ValidTo: DateTimeOffset.UtcNow.AddDays(30),
        Markets: new[] { "sa" },
        LandingQuery: null,
        NotesInternal: null,
        CampaignLink: new CampaignLinkRequest(linkKind, linkTargetId));
}
