using BackendApi.Features.Seeding;
using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Cms.Seeding;

/// <summary>
/// Reference-data seeder per spec 024 tasks T049 / data-model §8.
/// Idempotent insert of three <c>cms.market_schemas</c> rows
/// (<c>EG</c>, <c>KSA</c>, <c>*</c>) with V1 default policy values.
/// Re-running converges to a clean no-op.
/// </summary>
public sealed class CmsReferenceDataSeeder : ISeeder
{
    public string Name => "cms.reference-data";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => [];

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        var db = ctx.Services.GetRequiredService<CmsDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;
        var systemActor = Guid.Empty;

        await UpsertMarketSchemaAsync(db, BuildDefaultSchema("EG", systemActor, nowUtc), ct);
        await UpsertMarketSchemaAsync(db, BuildDefaultSchema("KSA", systemActor, nowUtc), ct);
        await UpsertMarketSchemaAsync(db, BuildDefaultSchema("*", systemActor, nowUtc), ct);
    }

    private static CmsMarketSchema BuildDefaultSchema(string marketCode, Guid actor, DateTimeOffset nowUtc) => new()
    {
        MarketCode = marketCode,
        BannerMaxLivePerSlot = 5,
        FeaturedSectionMaxReferences = 24,
        PreviewTokenDefaultTtlHours = 24,
        DraftStalenessAlertDays = 30,
        AssetGracePeriodDays = 7,
        LastEditedByActorId = actor,
        LastEditedAtUtc = nowUtc,
    };

    private static async Task UpsertMarketSchemaAsync(CmsDbContext db, CmsMarketSchema row, CancellationToken ct)
    {
        var existing = await db.MarketSchemas.FirstOrDefaultAsync(x => x.MarketCode == row.MarketCode, ct);
        if (existing is null)
        {
            await db.MarketSchemas.AddAsync(row, ct);
            await db.SaveChangesAsync(ct);
        }
        // Existing rows are NOT overwritten — V1 super_admin owns subsequent edits per FR-031.
    }
}
