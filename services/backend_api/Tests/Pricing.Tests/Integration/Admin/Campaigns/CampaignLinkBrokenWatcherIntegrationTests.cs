using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Pricing.Admin.Campaigns;
using BackendApi.Modules.Pricing.Admin.Coupons;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Admin.Campaigns;

/// <summary>
/// Spec 007-b T096 — end-to-end check that <c>CampaignLinkBrokenWatcher</c>
/// auto-marks a campaign as broken-link when the linked coupon is deactivated
/// via the public endpoint. Confirms FR-019 cross-module wiring rather than
/// the unit-level behaviour already covered by
/// <see cref="Subscribers.CampaignLinkBrokenWatcherTests"/>.
/// </summary>
[Collection("pricing-fixture")]
public sealed class CampaignLinkBrokenWatcherIntegrationTests(PricingTestFactory factory)
{
    [Fact]
    public async Task DeactivatingLinkedCoupon_AutoMarksReferencingCampaignBroken()
    {
        await factory.ResetDatabaseAsync();
        var client = await BuildOperatorClientAsync();

        // 1) Create + schedule a coupon with DisplayInBanners=true so we can
        //    link a campaign to it.
        var createCouponResp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons",
            new CreateCommercialCouponRequest(
                Code: $"WATCH-{Guid.NewGuid().ToString("N")[..8]}",
                Markets: new[] { "ksa" },
                Type: "percent_off",
                Value: 5,                     // below 30% threshold — gate stays off
                AmountOffMinor: null,
                CapMinor: 1_00_000,           // below 5M threshold
                PerCustomerLimit: 1,
                OverallLimit: 100,
                ExcludesRestricted: true,
                ValidFrom: DateTimeOffset.UtcNow.AddDays(-1),
                ValidTo: DateTimeOffset.UtcNow.AddDays(7),
                StacksWithPromotions: true,
                DisplayInBanners: true,
                Label: new("كوبون بانر", "Banner coupon"),
                Description: null));
        createCouponResp.EnsureSuccessStatusCode();
        var coupon = (await createCouponResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;

        var schedResp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/coupons/{coupon.Id:D}/schedule", new { });
        schedResp.EnsureSuccessStatusCode();

        // 2) Create the campaign and link it to the coupon.
        var createCampaignResp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/campaigns",
            new CreateCampaignRequest(
                Name: new CampaignBilingual("حملة المراقبة", "Watcher campaign"),
                ValidFrom: DateTimeOffset.UtcNow.AddDays(1),
                ValidTo: DateTimeOffset.UtcNow.AddDays(30),
                Markets: new[] { "sa" },
                LandingQuery: null,
                NotesInternal: null,
                CampaignLink: new CampaignLinkRequest("coupon", coupon.Id)));
        createCampaignResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var campaign = (await createCampaignResp.Content.ReadFromJsonAsync<CommercialCampaignResponse>())!;
        campaign.LinkBroken.Should().BeFalse();

        // 3) Deactivate the linked coupon.
        var deactResp = await client.PostAsJsonAsync(
            $"/v1/admin/commercial/coupons/{coupon.Id:D}/deactivate",
            new { ReasonNote = "operator paused for cross-module watcher test" });
        deactResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4) Watcher subscribes synchronously on the in-process bus, so the
        //    flip should be observable immediately after the deactivate call.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        var refreshed = await db.Campaigns.AsNoTracking()
            .SingleAsync(c => c.Id == campaign.Id);
        refreshed.LinkBroken.Should().BeTrue();
        refreshed.State.Should().Be(LifecycleState.Draft,
            "campaign must NOT auto-deactivate (FR-019), only the broken-link indicator flips");
    }

    private async Task<HttpClient> BuildOperatorClientAsync()
    {
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);
        return client;
    }
}
