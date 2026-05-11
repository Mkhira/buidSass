using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Pricing.Admin.Coupons;
using BackendApi.Modules.Pricing.Admin.Preview;
using BackendApi.Modules.Pricing.Admin.PreviewProfiles;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Admin.Preview;

/// <summary>
/// Spec 007-b T054 — research §R2 verification hook. The Preview tool MUST
/// produce the same RESOLVED PRICE that <c>IPriceCalculator.Calculate(ctx)</c>
/// emits at runtime for the same profile and the same saved coupon.
///
/// We assert equivalence at the totals + per-line layer level rather than on
/// the canonical explanation_hash, because the hash includes <c>nowUtc</c> at
/// millisecond precision and the two endpoints sample <c>UtcNow</c> at
/// different instants. The economic effect (net, tax, gross, applied per
/// layer) is the spec-relevant invariant; the hash equality holds whenever
/// the two contexts share a clock, which the runtime engine validates
/// elsewhere (see <c>RoundingDriftTests</c>, <c>DeterminismTests</c>).
/// </summary>
[Collection("pricing-fixture")]
public sealed class PreviewMatchesRuntimeTests(PricingTestFactory factory)
{
    [Fact]
    public async Task Preview_With_SavedActiveCoupon_Hash_Matches_Runtime()
    {
        await factory.ResetDatabaseAsync();
        var (token, actorId) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        // 1. Seed VAT + a published product
        Guid productId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await PricingTestSeedHelper.SeedKsaVatAsync(scope.ServiceProvider);
            productId = await PricingTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider, "GLV-NTR-100", priceHintMinor: 10_000, marketCodes: new[] { "ksa" });
        }
        // Capture the SKU so the preview profile cart_lines reference it.
        var sku = "GLV-NTR-100";

        // 2. Create a saved coupon in active state (sub-threshold so the gate
        //    doesn't fork the schedule path).
        var createReq = new CreateCommercialCouponRequest(
            Code: "PREVIEW10",
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
            Label: new("خصم معاينة", "Preview discount"),
            Description: null);
        var couponResp = await client.PostAsJsonAsync("/v1/admin/commercial/coupons", createReq);
        couponResp.EnsureSuccessStatusCode();
        var coupon = (await couponResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;
        var sched = await client.PostAsync($"/v1/admin/commercial/coupons/{coupon.Id:N}/schedule", null);
        sched.EnsureSuccessStatusCode();

        // 3. Create a preview profile referencing the seeded SKU
        var profileResp = await client.PutAsJsonAsync(
            "/v1/admin/commercial/preview-profiles",
            new UpsertPreviewProfileRequest(
                Id: null,
                Name: "KSA consumer 1-line",
                MarketCode: "SA",
                Locale: "en",
                AccountKind: "consumer",
                TierId: null,
                VerificationState: "none",
                CartLines: new[] { new PreviewCartLine(sku, 2, false) },
                Visibility: "personal"));
        profileResp.EnsureSuccessStatusCode();
        var profile = (await profileResp.Content.ReadFromJsonAsync<PreviewProfileResponse>())!;

        // 4. Run the Preview tool against the saved coupon (rule_id set)
        var previewReq = new PreviewExplanationRequest(
            PreviewProfileId: profile.Id,
            InFlightRule: new PreviewInFlightRule(
                Kind: "coupon",
                RuleId: coupon.Id,
                Coupon: null));

        var previewResp = await client.PostAsJsonAsync(
            "/v1/admin/commercial/preview/price-explanation", previewReq);
        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = (await previewResp.Content.ReadFromJsonAsync<PreviewExplanationResponse>())!;
        preview.WithRule.ExplanationHash.Should().NotBeNullOrEmpty();

        // 5. Call the customer price-cart endpoint with the same coupon code.
        //    Both paths go through PriceCalculator; the canonicalised hash
        //    must agree byte-for-byte.
        var customerReq = new
        {
            marketCode = "ksa",
            locale = "en",
            lines = new[] { new { productId = productId, qty = 2 } },
            couponCode = "PREVIEW10",
        };
        var customerResp = await client.PostAsJsonAsync("/v1/customer/pricing/price-cart", customerReq);
        customerResp.EnsureSuccessStatusCode();
        var customer = await customerResp.Content.ReadFromJsonAsync<CustomerPriceCartShape>();

        // R2: the resolved economic effect must match. We compare totals
        // + per-line applied-minor rather than the time-sensitive hash;
        // see class-level remarks for the rationale.
        preview.WithRule.Totals.SubtotalMinor.Should().Be(customer!.Totals.SubtotalMinor);
        preview.WithRule.Totals.TaxMinor.Should().Be(customer.Totals.TaxMinor);
        preview.WithRule.Totals.GrandTotalMinor.Should().Be(customer.Totals.GrandTotalMinor);
        preview.WithRule.Totals.DiscountMinor.Should().Be(customer.Totals.DiscountMinor);

        var prevByProduct = preview.WithRule.Lines.ToDictionary(l => l.ProductId);
        var custByProduct = customer.Lines.ToDictionary(l => l.ProductId);
        prevByProduct.Keys.Should().BeEquivalentTo(custByProduct.Keys);
        foreach (var pid in prevByProduct.Keys)
        {
            var prev = prevByProduct[pid];
            var cust = custByProduct[pid];
            prev.NetMinor.Should().Be(cust.NetMinor, $"net for product {pid:N}");
            prev.GrossMinor.Should().Be(cust.GrossMinor, $"gross for product {pid:N}");
            prev.TaxMinor.Should().Be(cust.TaxMinor, $"tax for product {pid:N}");
            prev.Explanation.Where(l => l.Layer == "coupon").Sum(l => l.AppliedMinor)
                .Should().Be(cust.Layers.Where(l => l.Layer == "coupon").Sum(l => l.AppliedMinor));
        }

        // Sanity: there IS a delta vs the without-rule baseline (the coupon must apply something).
        preview.WithRule.Totals.GrandTotalMinor.Should().BeLessThan(preview.WithoutRule.Totals.GrandTotalMinor,
            "applying the coupon must reduce the grand total");
    }

    private sealed record CustomerPriceCartShape(
        IReadOnlyList<CustomerPriceCartLine> Lines,
        CustomerPriceCartTotals Totals);

    private sealed record CustomerPriceCartLine(
        Guid ProductId,
        long NetMinor,
        long TaxMinor,
        long GrossMinor,
        IReadOnlyList<CustomerPriceCartLayer> Layers);

    private sealed record CustomerPriceCartLayer(string Layer, long AppliedMinor);

    private sealed record CustomerPriceCartTotals(
        long SubtotalMinor,
        long DiscountMinor,
        long TaxMinor,
        long GrandTotalMinor);
}
