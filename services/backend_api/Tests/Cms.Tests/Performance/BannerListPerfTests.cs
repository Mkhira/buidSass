using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Primitives;
using Cms.Tests.Contract.Infrastructure;
using FluentAssertions;

namespace Cms.Tests.Performance;

/// <summary>
/// T075 — SC-006 banner list performance test. With 1 000 live banners
/// seeded for market EG, the storefront list endpoint MUST return a 50-row
/// page in p95 ≤ 200 ms. The test boots the same Testcontainers Postgres
/// + WebApplicationFactory used by the contract suite, performs a 50-shot
/// warmup-+-measurement loop, and asserts the 95th percentile latency.
/// </summary>
[Trait("Category", "Performance")]
public sealed class BannerListPerfTests
    : IClassFixture<CmsApiFactory>, IAsyncLifetime
{
    private const int LiveBannerCount = 1_000;
    private const int RequestPageSize = 50;
    private const int Iterations = 50;
    private const double P95LatencyBudgetMs = 200.0;

    private readonly CmsApiFactory _factory;
    private readonly HttpClient _client;

    public BannerListPerfTests(CmsApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetCmsAsync();
        await SeedLiveBannersAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedLiveBannersAsync()
    {
        await using var ctx = _factory.NewDbContext();
        var nowUtc = DateTimeOffset.UtcNow;
        var slotKinds = new[] { "hero_top", "category_strip", "footer_strip", "home_secondary" };
        for (var i = 0; i < LiveBannerCount; i++)
        {
            ctx.BannerSlots.Add(new BannerSlot
            {
                Id = Guid.NewGuid(),
                SlotKindWire = slotKinds[i % slotKinds.Length],
                HeadlineEn = $"Banner #{i:D5}",
                HeadlineAr = $"بانر رقم {i:D5}",
                CtaKindWire = "none",
                CtaHealthWire = "not_applicable",
                MarketCode = "EG",
                StateWire = ContentLifecycleState.Live.ToWire(),
                PriorityWithinSlot = (i % 100) + 1,
                OwnerActorId = Guid.NewGuid(),
                CreatedAtUtc = nowUtc.AddMinutes(-i),
                EditorSaveAtUtc = nowUtc.AddMinutes(-i),
                PublishedAtUtc = nowUtc.AddMinutes(-i),
            });
            // Batch every 200 inserts to keep ChangeTracker shallow.
            if ((i + 1) % 200 == 0)
            {
                await ctx.SaveChangesAsync();
                ctx.ChangeTracker.Clear();
            }
        }
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Banner_list_p95_latency_under_budget_at_1000_live_banners()
    {
        // Warm up the JIT, EF Core query compilation cache, and Postgres
        // page cache. Without warmup, the first call dominates p95. Each
        // warm-up call also asserts the response body actually contains the
        // requested page size — ties the latency budget to real workload
        // (the endpoint must materialize 50 rows, not return an empty page).
        for (var i = 0; i < 5; i++)
        {
            var warm = await _client.GetAsync(
                $"/v1/storefront/cms/banner-slots?market=EG&locale=en&page_size={RequestPageSize}");
            warm.StatusCode.Should().Be(HttpStatusCode.OK);
            await AssertReturnedPageSizeAsync(warm);
        }

        var samples = new List<double>(Iterations);
        for (var i = 0; i < Iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            var resp = await _client.GetAsync(
                $"/v1/storefront/cms/banner-slots?market=EG&locale=en&page_size={RequestPageSize}");
            sw.Stop();
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            await AssertReturnedPageSizeAsync(resp);
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        // Inclusive p95 — for n=50, index 47 (0-based ceil(0.95*50)-1).
        var p95Index = (int)Math.Ceiling(0.95 * Iterations) - 1;
        var p95 = samples[p95Index];

        p95.Should().BeLessThanOrEqualTo(P95LatencyBudgetMs,
            $"SC-006 p95 latency budget is {P95LatencyBudgetMs} ms; observed {p95:F1} ms (samples: " +
            $"min={samples.First():F1}, median={samples[Iterations / 2]:F1}, max={samples.Last():F1})");
    }

    /// <summary>
    /// Reads the response body and asserts the returned items count matches
    /// the requested page size. Ties the perf assertion to actual workload
    /// — without this, an endpoint regression that returns an empty page
    /// would silently pass the latency budget.
    /// </summary>
    private static async Task AssertReturnedPageSizeAsync(HttpResponseMessage response)
    {
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        doc.TryGetProperty("items", out var items)
            .Should().BeTrue("response payload must include an 'items' array");
        items.GetArrayLength().Should().Be(RequestPageSize,
            $"banner list endpoint must materialize the requested page size ({RequestPageSize}) " +
            "for the latency budget to reflect real work");
    }
}
