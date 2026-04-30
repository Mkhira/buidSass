using BackendApi.Features.Seeding;
using BackendApi.Modules.Reviews.Entities;
using BackendApi.Modules.Reviews.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BackendApi.Modules.Reviews.Seeding;

/// <summary>
/// Reference-data seeder per spec 022 tasks T041 / data-model §2.7.
/// Idempotent insert of the KSA + EG market-schema rows (default policy values)
/// and a small bilingual seed wordlist. Re-running converges to a clean no-op.
/// </summary>
public sealed class ReviewsReferenceDataSeeder : ISeeder
{
    public string Name => "reviews.reference-data";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => [];

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        var db = ctx.Services.GetRequiredService<ReviewsDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;
        var systemActor = Guid.Empty;

        await UpsertMarketSchemaAsync(db, BuildDefaultSchema("SA", systemActor, nowUtc), ct);
        await UpsertMarketSchemaAsync(db, BuildDefaultSchema("EG", systemActor, nowUtc), ct);

        // Seed wordlist — small, deliberately editorial-grade. Real terms are
        // managed by reviews.policy_admin via PolicyAdmin endpoints; these
        // rows establish a non-empty initial state so US2 tests have something
        // to assert against.
        foreach (var (market, term) in SeedWordlist)
        {
            await TryInsertWordlistAsync(db, new ReviewsFilterWordlist
            {
                MarketCode = market,
                Term = term,
                CreatedByActorId = systemActor,
                CreatedAtUtc = nowUtc,
            }, ct);
        }
    }

    private static ReviewsMarketSchema BuildDefaultSchema(string marketCode, Guid actor, DateTimeOffset nowUtc) => new()
    {
        MarketCode = marketCode,
        EligibilityWindowDays = 180,
        EditWindowDays = 30,
        CommunityReportThreshold = 3,
        CommunityReportWindowDays = 30,
        ReportQualifyingAccountAgeDays = 14,
        ReportQualifyingRequiresVerifiedBuyer = true,
        PendingModerationSlaHours = 168,
        UpdatedAtUtc = nowUtc,
        UpdatedByActorId = actor,
    };

    private static async Task UpsertMarketSchemaAsync(
        ReviewsDbContext db,
        ReviewsMarketSchema schema,
        CancellationToken ct)
    {
        var exists = await db.MarketSchemas.AnyAsync(s => s.MarketCode == schema.MarketCode, ct);
        if (exists) return;

        db.MarketSchemas.Add(schema);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.Entry(schema).State = EntityState.Detached;
        }
    }

    private static async Task TryInsertWordlistAsync(
        ReviewsDbContext db,
        ReviewsFilterWordlist row,
        CancellationToken ct)
    {
        var exists = await db.Wordlists.AnyAsync(w => w.MarketCode == row.MarketCode && w.Term == row.Term, ct);
        if (exists) return;

        db.Wordlists.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.Entry(row).State = EntityState.Detached;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;

    /// <summary>
    /// Seed wordlist used to bootstrap moderation tests. Stored Arabic-normalized
    /// + lowercased to match the runtime profanity-filter comparison.
    /// </summary>
    private static readonly (string Market, string Term)[] SeedWordlist =
    {
        ("SA", "spam"),
        ("SA", "scam"),
        ("EG", "spam"),
        ("EG", "scam"),
    };
}
