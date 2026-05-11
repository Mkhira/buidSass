using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Pricing.Admin.Coupons;
using BackendApi.Modules.Pricing.Authorization;
using FluentAssertions;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Admin.Coupons;

/// <summary>
/// Spec 007-b T053 — covers the FR-004 active-state pricing-field lock and
/// the optimistic-concurrency If-Match guard from contract §2.2.
/// </summary>
[Collection("pricing-fixture")]
public sealed class UpdateCommercialCouponTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Update_NonPricingField_While_Active_Succeeds()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var draft = await CreateAndScheduleAsync(client);

        // Only the description field is touched — non-pricing per contract §2.2.
        var patch = new UpdateCommercialCouponRequest(
            Type: null, Value: null, AmountOffMinor: null, CapMinor: null,
            PerCustomerLimit: null, OverallLimit: null, ExcludesRestricted: null,
            Markets: null, ValidFrom: null, ValidTo: null,
            StacksWithPromotions: null, DisplayInBanners: null,
            Label: null, Description: new("وصف محدث", "Updated description"));

        var resp = await client.PatchAsJsonAsync(
            $"/v1/admin/commercial/coupons/{draft.Id:N}", patch);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<CommercialCouponResponse>();
        body!.Description!.En.Should().Be("Updated description");
        body.State.Should().Be("active");
    }

    [Fact]
    public async Task Update_PricingField_While_Active_Returns400_With_LockedReason()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var active = await CreateAndScheduleAsync(client);

        var patch = new UpdateCommercialCouponRequest(
            Type: null, Value: 20, AmountOffMinor: null, CapMinor: null,
            PerCustomerLimit: null, OverallLimit: null, ExcludesRestricted: null,
            Markets: null, ValidFrom: null, ValidTo: null,
            StacksWithPromotions: null, DisplayInBanners: null,
            Label: null, Description: null);

        var resp = await client.PatchAsJsonAsync(
            $"/v1/admin/commercial/coupons/{active.Id:N}", patch);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("coupon.locked.active_pricing_field");
    }

    [Fact]
    public async Task Update_With_Mismatched_IfMatch_Returns409_With_VersionConflict()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var draft = await CreateDraftAsync(client);

        var patch = new UpdateCommercialCouponRequest(
            Type: null, Value: null, AmountOffMinor: null, CapMinor: null,
            PerCustomerLimit: null, OverallLimit: null, ExcludesRestricted: null,
            Markets: null, ValidFrom: null, ValidTo: null,
            StacksWithPromotions: null, DisplayInBanners: null,
            Label: null, Description: new("forced", "forced"));

        // Stale row_version → second mutation must lose to optimistic-concurrency.
        var staleVersion = draft.RowVersion - 1;
        var req = new HttpRequestMessage(HttpMethod.Patch,
            $"/v1/admin/commercial/coupons/{draft.Id:N}")
        {
            Content = JsonContent.Create(patch),
        };
        // HttpClient enforces RFC-7232 ETag syntax on If-Match — bare numbers
        // are rejected with FormatException. Quote the value so the parser
        // accepts it; the server-side parser strips the quotes (see
        // AdminCommercialResponseFactory.TryParseIfMatch).
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{staleVersion}\"");

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("commercial.row.version_conflict");
    }

    private static async Task<CommercialCouponResponse> CreateAndScheduleAsync(HttpClient client)
    {
        var draft = await CreateDraftAsync(client);
        var sched = await client.PostAsync(
            $"/v1/admin/commercial/coupons/{draft.Id:N}/schedule", null);
        sched.EnsureSuccessStatusCode();
        return (await sched.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;
    }

    private static async Task<CommercialCouponResponse> CreateDraftAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/coupons",
            new CreateCommercialCouponRequest(
                Code: $"UPD{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                Markets: new[] { "ksa" },
                Type: "percent_off",
                Value: 5,
                AmountOffMinor: null,
                CapMinor: 1_000,
                PerCustomerLimit: 1,
                OverallLimit: 100,
                ExcludesRestricted: false,
                ValidFrom: DateTimeOffset.UtcNow.AddMinutes(-1),
                ValidTo: DateTimeOffset.UtcNow.AddDays(7),
                StacksWithPromotions: true,
                DisplayInBanners: false,
                Label: new("خصم تجريبي", "Test discount"),
                Description: null));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;
    }
}
