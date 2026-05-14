using System.Text.Json;
using BackendApi.Modules.Shipping.Domain;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using BackendApi.Modules.Shipping.Quote;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Shipping.Tests.Unit;

public sealed class ZoneResolverTests
{
    // AC-5 — out-of-zone returns null (no matching zone).
    // AC-4 — city-list primary match wins.
    // Research §1 — postal-code prefix tie-breaker (longest-prefix wins).

    [Fact]
    public async Task City_list_matches_take_precedence()
    {
        await using var db = NewInMemoryDb();
        SeedTwoZones(db);
        var resolver = new ZoneResolver(db);

        var zone = await resolver.ResolveAsync("SA", "Riyadh", "99999", CancellationToken.None);
        zone.Should().NotBeNull();
        zone!.NameEn.Should().Be("Riyadh");
    }

    [Fact]
    public async Task Out_of_zone_returns_null()
    {
        await using var db = NewInMemoryDb();
        SeedTwoZones(db);
        var resolver = new ZoneResolver(db);

        var zone = await resolver.ResolveAsync("SA", "Sharurah", "99999", CancellationToken.None);
        zone.Should().BeNull();
    }

    [Fact]
    public async Task Postal_code_longest_prefix_wins()
    {
        await using var db = NewInMemoryDb();
        // Two overlapping prefixes — 21 (Jeddah) and 213 (custom narrow).
        db.ShippingZones.AddRange(
            new ShippingZone
            {
                Id = Guid.NewGuid(),
                MarketCode = "SA",
                NameAr = "جدة",
                NameEn = "Jeddah",
                CityListJson = "[]",
                PostalCodePrefixesJson = JsonSerializer.Serialize(new[] { "21" }),
            },
            new ShippingZone
            {
                Id = Guid.NewGuid(),
                MarketCode = "SA",
                NameAr = "حي محدد",
                NameEn = "Specific district",
                CityListJson = "[]",
                PostalCodePrefixesJson = JsonSerializer.Serialize(new[] { "213" }),
            });
        await db.SaveChangesAsync();
        var resolver = new ZoneResolver(db);

        var zone = await resolver.ResolveAsync("SA", null, "21345", CancellationToken.None);
        zone.Should().NotBeNull();
        zone!.NameEn.Should().Be("Specific district");
    }

    [Fact]
    public async Task Invalid_market_returns_null()
    {
        await using var db = NewInMemoryDb();
        var resolver = new ZoneResolver(db);
        var zone = await resolver.ResolveAsync("US", "Riyadh", "11", CancellationToken.None);
        zone.Should().BeNull();
    }

    private static ShippingDbContext NewInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ShippingDbContext>()
            .UseInMemoryDatabase($"shipping-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ShippingDbContext(options);
    }

    private static void SeedTwoZones(ShippingDbContext db)
    {
        db.ShippingZones.AddRange(
            new ShippingZone
            {
                Id = Guid.NewGuid(),
                MarketCode = "SA",
                NameAr = "الرياض",
                NameEn = "Riyadh",
                CityListJson = JsonSerializer.Serialize(new[] { "riyadh", "diriyah" }),
                PostalCodePrefixesJson = JsonSerializer.Serialize(new[] { "11" }),
            },
            new ShippingZone
            {
                Id = Guid.NewGuid(),
                MarketCode = "SA",
                NameAr = "جدة",
                NameEn = "Jeddah",
                CityListJson = JsonSerializer.Serialize(new[] { "jeddah" }),
                PostalCodePrefixesJson = JsonSerializer.Serialize(new[] { "21", "22" }),
            });
        db.SaveChanges();
    }
}
