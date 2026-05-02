using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Services;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Shared.Testing;
using FluentAssertions;

namespace Cms.Tests.Unit;

public sealed class FeaturedSectionResolverTests
{
    private readonly FakeCatalogProductReadContract _products = new();
    private readonly FakeCatalogCategoryReadContract _categories = new();
    private readonly FakeCatalogBundleReadContract _bundles = new();

    private FeaturedSectionResolver Resolver() => new(_products, _categories, _bundles);

    private static FeaturedSection Section(string referencesJson) => new()
    {
        Id = Guid.NewGuid(),
        SectionKindWire = "home_top",
        ReferencesJson = referencesJson,
        MarketCode = "EG",
        StateWire = "live",
    };

    [Fact]
    public async Task Empty_references_returns_empty_resolution()
    {
        var section = Section("[]");

        var res = await Resolver().ResolveAsync(section, "ar", CancellationToken.None);

        res.TotalReferences.Should().Be(0);
        res.TotalResolved.Should().Be(0);
        res.OmittedDueToUnavailableReferences.Should().BeFalse();
    }

    [Fact]
    public async Task All_available_references_resolve_completely()
    {
        var p = Guid.NewGuid();
        var c = Guid.NewGuid();
        var b = Guid.NewGuid();
        var section = Section($@"[
            {{""kind"":""product"",""id"":""{p}""}},
            {{""kind"":""category"",""id"":""{c}""}},
            {{""kind"":""bundle"",""id"":""{b}""}}
        ]");

        var res = await Resolver().ResolveAsync(section, "ar", CancellationToken.None);

        res.TotalReferences.Should().Be(3);
        res.TotalResolved.Should().Be(3);
        res.TotalUnavailable.Should().Be(0);
        res.OmittedDueToUnavailableReferences.Should().BeFalse();
    }

    [Fact]
    public async Task Unavailable_reference_is_filtered_and_counted()
    {
        var pAvail = Guid.NewGuid();
        var pGone = Guid.NewGuid();
        _products.WithProduct(new CatalogProductRead(
            ProductId: pGone, MarketCode: "EG",
            DisplayNameAr: "AR", DisplayNameEn: "EN",
            VendorId: null, IsAvailable: false,
            UnavailableReason: LinkedEntityUnavailableReason.Archived));
        var section = Section($@"[
            {{""kind"":""product"",""id"":""{pAvail}""}},
            {{""kind"":""product"",""id"":""{pGone}""}}
        ]");

        var res = await Resolver().ResolveAsync(section, "en", CancellationToken.None);

        res.TotalReferences.Should().Be(2);
        res.TotalResolved.Should().Be(1);
        res.TotalUnavailable.Should().Be(1);
        res.OmittedDueToUnavailableReferences.Should().BeFalse();
        res.Resolved[0].Id.Should().Be(pAvail);
    }

    [Fact]
    public async Task All_unavailable_references_marks_section_omitted()
    {
        var p = Guid.NewGuid();
        _products.WithProduct(new CatalogProductRead(
            ProductId: p, MarketCode: "EG",
            DisplayNameAr: "AR", DisplayNameEn: "EN",
            VendorId: null, IsAvailable: false,
            UnavailableReason: LinkedEntityUnavailableReason.NotFound));
        var section = Section($@"[{{""kind"":""product"",""id"":""{p}""}}]");

        var res = await Resolver().ResolveAsync(section, "ar", CancellationToken.None);

        res.OmittedDueToUnavailableReferences.Should().BeTrue();
        res.TotalResolved.Should().Be(0);
    }

    [Fact]
    public async Task Unsupported_kinds_skipped_silently()
    {
        var section = Section(@"[{""kind"":""sku"",""id"":""00000000-0000-0000-0000-000000000001""}]");

        var res = await Resolver().ResolveAsync(section, "ar", CancellationToken.None);

        res.TotalReferences.Should().Be(0);
    }

    [Fact]
    public async Task Malformed_jsonb_returns_empty_resolution()
    {
        var section = Section("not-json");

        var res = await Resolver().ResolveAsync(section, "ar", CancellationToken.None);

        res.TotalReferences.Should().Be(0);
    }

    [Fact]
    public async Task Locale_drives_display_name_selection()
    {
        var p = Guid.NewGuid();
        _products.WithProduct(new CatalogProductRead(
            ProductId: p, MarketCode: "EG",
            DisplayNameAr: "اسم", DisplayNameEn: "Name",
            VendorId: null, IsAvailable: true,
            UnavailableReason: null));
        var section = Section($@"[{{""kind"":""product"",""id"":""{p}""}}]");

        var ar = await Resolver().ResolveAsync(section, "ar", CancellationToken.None);
        var en = await Resolver().ResolveAsync(section, "en", CancellationToken.None);

        ar.Resolved[0].DisplayName.Should().Be("اسم");
        en.Resolved[0].DisplayName.Should().Be("Name");
    }
}
