using System.Net;
using System.Text.Json;
using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Shared.Testing;
using Cms.Tests.Contract.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Cms.Tests.Contract.Storefront;

/// <summary>
/// T072 — contract test for <c>GET /v1/storefront/cms/featured-sections</c>
/// per spec 024 contract §7.2. Asserts the live-resolved response shape
/// (<c>{sectionId, referencesResolved, totalReferences, totalResolved,
/// totalUnavailable}</c>) and the <c>omittedDueToUnavailableReferences=true</c>
/// flag on fully-broken sections (FR-019).
/// </summary>
public sealed class ListFeaturedSectionsContractTests
    : IClassFixture<CmsApiFactory>, IAsyncLifetime
{
    private readonly CmsApiFactory _factory;
    private readonly HttpClient _client;

    public ListFeaturedSectionsContractTests(CmsApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetCmsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GET_featured_sections_returns_live_resolved_envelope()
    {
        await using var ctx = _factory.NewDbContext();
        var nowUtc = DateTimeOffset.UtcNow;

        var availableProductId = Guid.NewGuid();
        var refs = JsonSerializer.Serialize(new[]
        {
            new { kind = "product", id = availableProductId },
        });

        ctx.FeaturedSections.Add(new FeaturedSection
        {
            Id = Guid.NewGuid(),
            SectionKindWire = "home_top",
            TitleEn = "Top Picks",
            TitleAr = "الأكثر مبيعًا",
            ReferencesJson = refs,
            MarketCode = "EG",
            StateWire = ContentLifecycleState.Live.ToWire(),
            DisplayPriority = 100,
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
            PublishedAtUtc = nowUtc,
        });
        await ctx.SaveChangesAsync();

        var resp = await _client.GetAsync(
            "/v1/storefront/cms/featured-sections?market=EG&locale=en");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await resp.Content.ReadAsStringAsync();
        // Per the response record, properties serialize as camelCase.
        json.Should().Contain("\"sectionId\"");
        json.Should().Contain("\"referencesResolved\"");
        json.Should().Contain("\"totalReferences\"");
        json.Should().Contain("\"totalResolved\"");
        json.Should().Contain("\"totalUnavailable\"");
        json.Should().Contain("\"omittedDueToUnavailableReferences\"");
        // The fake catalog returns IsAvailable=true for unknown ids → 1/1 resolved.
        json.Should().Contain("\"totalReferences\":1");
        json.Should().Contain("\"totalResolved\":1");
        json.Should().Contain("\"totalUnavailable\":0");
    }

    [Fact]
    public async Task GET_featured_sections_marks_fully_broken_when_all_refs_unavailable()
    {
        // Stub the FakeCatalog to mark a specific product id as unavailable,
        // then publish a section whose only ref is that id.
        var unavailableId = Guid.NewGuid();
        var products = (FakeCatalogProductReadContract)
            _factory.Services.GetRequiredService<ICatalogProductReadContract>();
        products.WithProduct(new CatalogProductRead(
            ProductId: unavailableId,
            MarketCode: "EG",
            DisplayNameAr: "—",
            DisplayNameEn: "—",
            VendorId: null,
            IsAvailable: false,
            UnavailableReason: LinkedEntityUnavailableReason.NotFound));

        await using var ctx = _factory.NewDbContext();
        var nowUtc = DateTimeOffset.UtcNow;
        ctx.FeaturedSections.Add(new FeaturedSection
        {
            Id = Guid.NewGuid(),
            SectionKindWire = "home_mid",
            TitleEn = "Recommended",
            TitleAr = "موصى",
            ReferencesJson = JsonSerializer.Serialize(new[]
            {
                new { kind = "product", id = unavailableId },
            }),
            MarketCode = "EG",
            StateWire = ContentLifecycleState.Live.ToWire(),
            DisplayPriority = 200,
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
            PublishedAtUtc = nowUtc,
        });
        await ctx.SaveChangesAsync();

        var resp = await _client.GetAsync(
            "/v1/storefront/cms/featured-sections?market=EG&locale=en");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadAsStringAsync();

        // Find the home_mid section in the payload (separated from other tests
        // by a unique displayPriority + section_kind filter).
        var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("sectionKind").GetString() == "home_mid")
            .ToList();
        items.Should().HaveCount(1);
        var section = items[0];
        section.GetProperty("totalReferences").GetInt32().Should().Be(1);
        section.GetProperty("totalResolved").GetInt32().Should().Be(0);
        section.GetProperty("totalUnavailable").GetInt32().Should().Be(1);
        section.GetProperty("omittedDueToUnavailableReferences").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GET_featured_sections_returns_400_on_unsupported_market()
    {
        var resp = await _client.GetAsync(
            "/v1/storefront/cms/featured-sections?market=FR&locale=en");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("cms.storefront.market_unsupported");
    }
}
