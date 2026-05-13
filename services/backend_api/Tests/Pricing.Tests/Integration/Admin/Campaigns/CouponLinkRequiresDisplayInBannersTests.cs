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
/// Spec 007-b T095 — linking a campaign to a Coupon with
/// <c>display_in_banners=false</c> returns 400 with reason
/// <c>campaign.link.coupon_not_displayable</c>. The operator must toggle the
/// coupon's banner flag first before a campaign can wrap it (US4 acceptance
/// scenario 4).
/// </summary>
[Collection("pricing-fixture")]
public sealed class CouponLinkRequiresDisplayInBannersTests(PricingTestFactory factory)
{
    [Fact]
    public async Task LinkingCouponWithDisplayInBannersFalse_Returns400()
    {
        await factory.ResetDatabaseAsync();
        var client = await BuildOperatorClientAsync();
        var couponId = await SeedCouponAsync(displayInBanners: false);

        var resp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/campaigns",
            BuildCampaignRequest("coupon", couponId));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain(CommercialReasonCode.CampaignLinkCouponNotDisplayable);
    }

    [Fact]
    public async Task LinkingCouponWithDisplayInBannersTrue_Succeeds()
    {
        await factory.ResetDatabaseAsync();
        var client = await BuildOperatorClientAsync();
        var couponId = await SeedCouponAsync(displayInBanners: true);

        var resp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/campaigns",
            BuildCampaignRequest("coupon", couponId));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<CommercialCampaignResponse>();
        body!.CampaignLink!.Kind.Should().Be("coupon");
        body.CampaignLink.TargetId.Should().Be(couponId);
    }

    private async Task<HttpClient> BuildOperatorClientAsync()
    {
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);
        return client;
    }

    private async Task<Guid> SeedCouponAsync(bool displayInBanners)
    {
        var id = Guid.NewGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        db.Coupons.Add(new Coupon
        {
            Id = id,
            Code = $"BANNER-TEST-{Guid.NewGuid():N}".Substring(0, 24),
            Kind = "percent",
            Value = 10,
            CapMinor = 1_00_000,
            PerCustomerLimit = 1,
            OverallLimit = 100,
            ExcludesRestricted = false,
            StacksWithPromotions = true,
            MarketCodes = new[] { "sa" },
            ValidFrom = DateTimeOffset.UtcNow.AddDays(-1),
            ValidTo = DateTimeOffset.UtcNow.AddDays(7),
            DisplayInBanners = displayInBanners,
            LabelAr = "اختبار البانر",
            LabelEn = "Banner-eligibility test coupon",
            State = LifecycleState.Active,
            StateChangedAtUtc = DateTimeOffset.UtcNow,
            StateChangedByActorId = CommercialPermissions.SystemActorId,
            AuthorActorId = CommercialPermissions.SystemActorId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static CreateCampaignRequest BuildCampaignRequest(string linkKind, Guid linkTargetId) => new(
        Name: new CampaignBilingual("حملة كوبون البانر", "Banner-coupon campaign"),
        ValidFrom: DateTimeOffset.UtcNow.AddDays(1),
        ValidTo: DateTimeOffset.UtcNow.AddDays(30),
        Markets: new[] { "sa" },
        LandingQuery: null,
        NotesInternal: null,
        CampaignLink: new CampaignLinkRequest(linkKind, linkTargetId));
}
