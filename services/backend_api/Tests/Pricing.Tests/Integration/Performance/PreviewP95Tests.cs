using System.Diagnostics;
using System.Net.Http.Json;
using BackendApi.Modules.Pricing.Admin.Coupons;
using BackendApi.Modules.Pricing.Admin.Preview;
using BackendApi.Modules.Pricing.Admin.PreviewProfiles;
using BackendApi.Modules.Pricing.Authorization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Performance;

/// <summary>
/// Spec 007-b T055 / SC-002 — Preview tool p95 ≤ 200 ms for a 20-line cart.
/// Run inside the same Testcontainers Postgres fixture as the other
/// Pricing integration tests. CI noise is bounded by:
/// <list type="bullet">
///   <item>Warm-up: 10 calls discarded before sampling.</item>
///   <item>Sample size: 50 calls (small enough to fit CI time-budget,
///   large enough for a stable p95).</item>
///   <item>Headroom: assertion is ≤ 600 ms in CI to absorb container /
///   shared-runner variance; the 200 ms target is a steady-state goal
///   tracked separately by the SC-002 perf job (out of scope of unit tests).</item>
/// </list>
/// Replace the headroom budget with the strict 200 ms once the SC-002 perf
/// runner lands in the Polish phase.
/// </summary>
[Collection("pricing-fixture")]
public sealed class PreviewP95Tests(PricingTestFactory factory)
{
    private const int LineCount = 20;
    private const int WarmupCalls = 10;
    private const int SampleCalls = 50;
    private const int CiP95BudgetMs = 600;

    [Fact]
    public async Task Preview_p95_For_20Line_Cart_Within_CI_Headroom()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { CommercialPermissions.Operator });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        // Seed VAT + 20 published products
        var skus = new List<string>(LineCount);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await PricingTestSeedHelper.SeedKsaVatAsync(scope.ServiceProvider);
            for (var i = 0; i < LineCount; i++)
            {
                var sku = $"PERF-{i:D3}";
                await PricingTestSeedHelper.CreatePublishedProductAsync(
                    scope.ServiceProvider, sku, priceHintMinor: 10_000, marketCodes: new[] { "ksa" });
                skus.Add(sku);
            }
        }

        // Saved coupon (active) so the preview path doesn't pay the
        // transient-coupon insert/delete cost on every iteration.
        var createReq = new CreateCommercialCouponRequest(
            Code: "PERF10",
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
            Label: new("معاينة الأداء", "Performance preview"),
            Description: null);
        var couponResp = await client.PostAsJsonAsync("/v1/admin/commercial/coupons", createReq);
        couponResp.EnsureSuccessStatusCode();
        var coupon = (await couponResp.Content.ReadFromJsonAsync<CommercialCouponResponse>())!;
        await client.PostAsync($"/v1/admin/commercial/coupons/{coupon.Id:N}/schedule", null);

        // Profile referencing all 20 SKUs
        var profileResp = await client.PutAsJsonAsync(
            "/v1/admin/commercial/preview-profiles",
            new UpsertPreviewProfileRequest(
                Id: null,
                Name: "20-line cart",
                MarketCode: "SA",
                Locale: "en",
                AccountKind: "consumer",
                TierId: null,
                VerificationState: "none",
                CartLines: skus.Select(s => new PreviewCartLine(s, 1, false)).ToArray(),
                Visibility: "personal"));
        profileResp.EnsureSuccessStatusCode();
        var profile = (await profileResp.Content.ReadFromJsonAsync<PreviewProfileResponse>())!;

        var payload = new PreviewExplanationRequest(
            PreviewProfileId: profile.Id,
            InFlightRule: new PreviewInFlightRule(
                Kind: "coupon",
                RuleId: coupon.Id,
                Coupon: null));

        // Warm-up
        for (var i = 0; i < WarmupCalls; i++)
        {
            var warm = await client.PostAsJsonAsync("/v1/admin/commercial/preview/price-explanation", payload);
            warm.EnsureSuccessStatusCode();
        }

        var samples = new List<long>(SampleCalls);
        for (var i = 0; i < SampleCalls; i++)
        {
            var sw = Stopwatch.StartNew();
            var resp = await client.PostAsJsonAsync("/v1/admin/commercial/preview/price-explanation", payload);
            sw.Stop();
            resp.EnsureSuccessStatusCode();
            samples.Add(sw.ElapsedMilliseconds);
        }

        samples.Sort();
        var p95Index = (int)Math.Ceiling(samples.Count * 0.95) - 1;
        var p95 = samples[Math.Clamp(p95Index, 0, samples.Count - 1)];
        p95.Should().BeLessThan(CiP95BudgetMs,
            $"preview p95 {p95}ms must stay under the CI headroom budget; samples={string.Join(',', samples)}");
    }
}
