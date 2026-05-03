using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Shared.Testing;
using Cms.Tests.Contract.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Cms.Tests.Performance;

/// <summary>
/// T076 — SC-007 featured-section resolution performance test. With 10 000
/// catalog products primed in the in-process catalog stub and a featured
/// section holding 24 references, the storefront resolution path MUST
/// return p95 ≤ 300 ms. Resolution runs all 24 catalog reads in parallel
/// per spec 024 research §R4 — this test guards against a regression that
/// reverts to a serial loop.
/// </summary>
[Trait("Category", "Performance")]
public sealed class FeaturedSectionResolutionPerfTests
    : IClassFixture<CmsApiFactory>, IAsyncLifetime
{
    private const int CatalogProductCount = 10_000;
    private const int ReferencesPerSection = 24;
    private const int Iterations = 50;
    private const double P95LatencyBudgetMs = 300.0;

    private readonly CmsApiFactory _factory;
    private readonly HttpClient _client;

    public FeaturedSectionResolutionPerfTests(CmsApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetCmsAsync();
        await PrimeAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Guid[]? _selectedRefs;

    private async Task PrimeAsync()
    {
        var products = (FakeCatalogProductReadContract)
            _factory.Services.GetRequiredService<ICatalogProductReadContract>();

        // Prime 10 000 products in the in-process stub. The stub's default
        // path returns IsAvailable=true for unknown ids, but a real Catalog
        // module would have an indexed lookup; we explicitly populate the
        // stub to mimic that O(N) search-space.
        var rnd = new Random(42);
        var allIds = new Guid[CatalogProductCount];
        for (var i = 0; i < CatalogProductCount; i++)
        {
            allIds[i] = Guid.NewGuid();
            products.WithProduct(new CatalogProductRead(
                ProductId: allIds[i],
                MarketCode: "EG",
                DisplayNameAr: $"منتج {i:D5}",
                DisplayNameEn: $"Product {i:D5}",
                VendorId: null,
                IsAvailable: true,
                UnavailableReason: null));
        }

        // Pick 24 random refs out of 10 000 to populate the featured section.
        _selectedRefs = new Guid[ReferencesPerSection];
        for (var i = 0; i < ReferencesPerSection; i++)
        {
            _selectedRefs[i] = allIds[rnd.Next(0, CatalogProductCount)];
        }
        var refsJson = JsonSerializer.Serialize(
            _selectedRefs.Select(id => new { kind = "product", id }).ToArray());

        await using var ctx = _factory.NewDbContext();
        var nowUtc = DateTimeOffset.UtcNow;
        ctx.FeaturedSections.Add(new FeaturedSection
        {
            Id = Guid.NewGuid(),
            SectionKindWire = "home_top",
            TitleEn = "Top Picks",
            TitleAr = "الأكثر مبيعًا",
            ReferencesJson = refsJson,
            MarketCode = "EG",
            StateWire = ContentLifecycleState.Live.ToWire(),
            DisplayPriority = 100,
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
            PublishedAtUtc = nowUtc,
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task FeaturedSection_resolution_p95_latency_under_budget_with_24_refs_over_10k_catalog()
    {
        // Warmup. Each warm-up call also asserts the response body actually
        // resolved all 24 references — ties latency to real work (a
        // regression that short-circuits resolution and returns an empty
        // section would silently pass the budget without this guard).
        for (var i = 0; i < 5; i++)
        {
            var warm = await _client.GetAsync(
                "/v1/storefront/cms/featured-sections?market=EG&locale=en");
            warm.StatusCode.Should().Be(HttpStatusCode.OK);
            await AssertResolutionBodyAsync(warm);
        }

        var samples = new List<double>(Iterations);
        for (var i = 0; i < Iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            var resp = await _client.GetAsync(
                "/v1/storefront/cms/featured-sections?market=EG&locale=en");
            sw.Stop();
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            await AssertResolutionBodyAsync(resp);
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        var p95Index = (int)Math.Ceiling(0.95 * Iterations) - 1;
        var p95 = samples[p95Index];
        p95.Should().BeLessThanOrEqualTo(P95LatencyBudgetMs,
            $"SC-007 p95 latency budget is {P95LatencyBudgetMs} ms; observed {p95:F1} ms (samples: " +
            $"min={samples.First():F1}, median={samples[Iterations / 2]:F1}, max={samples.Last():F1})");
    }

    /// <summary>
    /// Reads the response body and asserts the section resolved all
    /// references (totalResolved == 24, the seeded ReferencesPerSection).
    /// Without this, an endpoint regression that returns an empty section
    /// or short-circuits resolution would silently pass the latency budget.
    /// </summary>
    private static async Task AssertResolutionBodyAsync(HttpResponseMessage response)
    {
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        doc.TryGetProperty("items", out var items)
            .Should().BeTrue("response payload must include an 'items' array");
        items.GetArrayLength().Should().BeGreaterThan(0,
            "at least one featured section must be returned for the EG storefront");
        var first = items[0];
        first.TryGetProperty("totalResolved", out var totalResolved)
            .Should().BeTrue("featured-section item must surface totalResolved");
        totalResolved.GetInt32().Should().Be(ReferencesPerSection,
            $"resolution must materialize all {ReferencesPerSection} references for the latency " +
            "budget to reflect real work — a regression that short-circuits resolution would " +
            "silently pass without this assertion");
    }
}
