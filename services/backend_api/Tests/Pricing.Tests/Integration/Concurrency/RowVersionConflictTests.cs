using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Pricing.Admin.Coupons;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using FluentAssertions;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Concurrency;

/// <summary>
/// Spec 007-b T147 — two operators editing the same coupon hit the
/// row-version (xmin) guard. The first <c>PATCH</c> with a stale
/// <c>If-Match</c> succeeds; a subsequent <c>PATCH</c> with the same stale
/// rowVersion returns 409 with <c>commercial.row.version_conflict</c>.
/// </summary>
[Collection("pricing-fixture")]
public sealed class RowVersionConflictTests(PricingTestFactory factory)
{
    [Fact]
    public async Task ConcurrentPatch_WithSameStaleRowVersion_SecondReturns409()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        // Create the coupon — operatorA + operatorB read this same row version.
        var createResp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons",
            new CreateCommercialCouponRequest(
                Code: "ROWVER-TEST",
                Markets: new[] { "ksa" },
                Type: "percent_off",
                Value: 5,
                AmountOffMinor: null,
                CapMinor: 1_00_000,
                PerCustomerLimit: 1,
                OverallLimit: 100,
                ExcludesRestricted: true,
                ValidFrom: DateTimeOffset.UtcNow.AddDays(1),
                ValidTo: DateTimeOffset.UtcNow.AddDays(7),
                StacksWithPromotions: true,
                DisplayInBanners: false,
                Label: new("اختبار النسخة", "Row-version test"),
                Description: null));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var coupon = (await createResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;
        var sharedRowVersion = coupon.RowVersion;

        // First PATCH succeeds — uses the original row version.
        var firstPatch = new HttpRequestMessage(HttpMethod.Patch,
            $"/v1/admin/commercial/coupons/{coupon.Id:D}")
        {
            Content = JsonContent.Create(new UpdateCommercialCouponRequest(
                Type: null,
                Value: null,
                AmountOffMinor: null,
                CapMinor: null,
                PerCustomerLimit: null,
                OverallLimit: null,
                ExcludesRestricted: null,
                Markets: null,
                ValidFrom: null,
                ValidTo: null,
                StacksWithPromotions: null,
                DisplayInBanners: null,
                Label: new CommercialCouponBilingual("الاسم الأول", "First update"),
                Description: null)),
        };
        firstPatch.Headers.Add("If-Match", $"\"{sharedRowVersion}\"");
        var firstResp = await client.SendAsync(firstPatch);
        firstResp.StatusCode.Should().Be(HttpStatusCode.OK, "first PATCH must succeed");

        // Second PATCH uses the same stale row version — the xmin has changed.
        var secondPatch = new HttpRequestMessage(HttpMethod.Patch,
            $"/v1/admin/commercial/coupons/{coupon.Id:D}")
        {
            Content = JsonContent.Create(new UpdateCommercialCouponRequest(
                Type: null,
                Value: null,
                AmountOffMinor: null,
                CapMinor: null,
                PerCustomerLimit: null,
                OverallLimit: null,
                ExcludesRestricted: null,
                Markets: null,
                ValidFrom: null,
                ValidTo: null,
                StacksWithPromotions: null,
                DisplayInBanners: null,
                Label: new CommercialCouponBilingual("الاسم الثاني", "Second update"),
                Description: null)),
        };
        secondPatch.Headers.Add("If-Match", $"\"{sharedRowVersion}\"");
        var secondResp = await client.SendAsync(secondPatch);

        secondResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await secondResp.Content.ReadAsStringAsync())
            .Should().Contain(CommercialReasonCode.CommercialRowVersionConflict);
    }
}
