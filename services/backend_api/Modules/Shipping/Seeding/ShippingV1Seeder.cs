using System.Text.Json;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Shipping.Domain;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Shipping.Seeding;

/// <summary>
/// T058 — V1 reference-data seeder. Idempotent: every Add is guarded by an
/// existence check on a stable natural key. Safe in Dev / Staging / Production
/// (SeedGuard short-circuits production-only paths upstream).
/// Seeds the per-market schemas (SA, EG) + four reference zones
/// (Riyadh, Jeddah, Greater Cairo, Alexandria) so the Shipping module has
/// something to quote against immediately after migration.
/// </summary>
public sealed class ShippingV1Seeder : ISeeder
{
    public string Name => "shipping.reference-data";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        var db = ctx.Services.GetRequiredService<ShippingDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;
        await SeedMarketSchemasAsync(db, nowUtc, ct);
        await SeedZonesAsync(db, nowUtc, ct);
    }

    private static async Task SeedMarketSchemasAsync(ShippingDbContext db, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var present = await db.MarketSchemas.Select(s => s.MarketCode).ToListAsync(ct);
        var byCode = present.ToHashSet(StringComparer.Ordinal);
        if (!byCode.Contains(ShippingConstants.Markets.SA))
        {
            db.MarketSchemas.Add(new ShippingMarketSchema
            {
                MarketCode = ShippingConstants.Markets.SA,
                PostalCodeRegex = @"^\d{5}$",
                DefaultCurrency = ShippingConstants.Currencies.SAR,
                DefaultEtaDaysMin = 2,
                DefaultEtaDaysMax = 4,
                SlaBreachThresholdHours = 96,
                UpdatedAt = nowUtc,
            });
        }
        if (!byCode.Contains(ShippingConstants.Markets.EG))
        {
            db.MarketSchemas.Add(new ShippingMarketSchema
            {
                MarketCode = ShippingConstants.Markets.EG,
                PostalCodeRegex = @"^\d{5}$",
                DefaultCurrency = ShippingConstants.Currencies.EGP,
                DefaultEtaDaysMin = 3,
                DefaultEtaDaysMax = 6,
                SlaBreachThresholdHours = 120,
                UpdatedAt = nowUtc,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedZonesAsync(ShippingDbContext db, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var existing = await db.ShippingZones
            .Where(z => z.DeletedAt == null)
            .Select(z => new { z.MarketCode, z.NameEn })
            .ToListAsync(ct);
        var existingKeys = existing
            .Select(e => (e.MarketCode, e.NameEn))
            .ToHashSet();

        var seeds = new[]
        {
            (Market: ShippingConstants.Markets.SA, NameAr: "الرياض", NameEn: "Riyadh",
             Postal: new[] { "11" }, Cities: new[] { "riyadh", "diriyah" }),
            (Market: ShippingConstants.Markets.SA, NameAr: "جدة", NameEn: "Jeddah",
             Postal: new[] { "21", "22" }, Cities: new[] { "jeddah" }),
            (Market: ShippingConstants.Markets.EG, NameAr: "القاهرة الكبرى", NameEn: "Greater Cairo",
             Postal: new[] { "11", "12" }, Cities: new[] { "cairo", "giza" }),
            (Market: ShippingConstants.Markets.EG, NameAr: "الإسكندرية", NameEn: "Alexandria",
             Postal: new[] { "21" }, Cities: new[] { "alexandria" }),
        };

        foreach (var s in seeds)
        {
            if (existingKeys.Contains((s.Market, s.NameEn))) continue;
            db.ShippingZones.Add(new ShippingZone
            {
                Id = Guid.NewGuid(),
                MarketCode = s.Market,
                NameAr = s.NameAr,
                NameEn = s.NameEn,
                PostalCodePrefixesJson = JsonSerializer.Serialize(s.Postal),
                CityListJson = JsonSerializer.Serialize(s.Cities),
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
            });
        }
        await db.SaveChangesAsync(ct);
    }
}
