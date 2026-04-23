using BackendApi.Features.Seeding;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Pricing.Seeding;

public sealed class PricingReferenceDataSeeder : ISeeder
{
    public string Name => "pricing.reference-data";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => [];

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        var db = ctx.Services.GetRequiredService<PricingDbContext>();
        var epoch = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        async Task UpsertRateAsync(string market, string kind, int bps)
        {
            var exists = await db.TaxRates.AnyAsync(
                r => r.MarketCode == market && r.Kind == kind && r.EffectiveFrom == epoch, ct);
            if (exists)
            {
                return;
            }
            db.TaxRates.Add(new TaxRate
            {
                Id = Guid.NewGuid(),
                MarketCode = market,
                Kind = kind,
                RateBps = bps,
                EffectiveFrom = epoch,
                EffectiveTo = null,
                CreatedByAccountId = null,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await UpsertRateAsync("ksa", "vat", 1500);
        await UpsertRateAsync("eg", "vat", 1400);
        await db.SaveChangesAsync(ct);
    }
}
