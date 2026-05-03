using BackendApi.Features.Seeding;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BackendApi.Modules.B2B.Seeding;

/// <summary>
/// Spec 021 reference-data seeder (quickstart §2 / tasks T048). Idempotent INSERT of the
/// KSA + EG market schemas (version 1) plus a non-PII <c>quotes-b2b-v1</c> stub seed
/// surface (kept in this same file for now — companies / memberships / quotes only land
/// once the user-story slices arrive).
///
/// PII-clean by construction: no real EG/KSA phone numbers, no consumer email domains,
/// no 14-digit national-IDs. The seed surface here only writes <see cref="QuoteMarketSchema"/>
/// rows (no PII-shaped fields). The future <c>QuotesB2BV1DevSeeder</c> (US7) will follow
/// the same convention.
/// </summary>
public sealed class B2BReferenceDataSeeder : ISeeder
{
    public string Name => "b2b.reference-data";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => [];

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        var db = ctx.Services.GetRequiredService<B2BDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;

        await TryInsertAsync(db, BuildSchema(marketCode: "ksa", version: 1, effectiveFrom: nowUtc), ct);
        await TryInsertAsync(db, BuildSchema(marketCode: "eg", version: 1, effectiveFrom: nowUtc), ct);
    }

    private static async Task TryInsertAsync(
        B2BDbContext db,
        QuoteMarketSchema schema,
        CancellationToken ct)
    {
        // Cheap pre-check first — the common path (already seeded) avoids an exception
        // and keeps logs quiet. Mirrors the spec 020 VerificationReferenceDataSeeder pattern.
        var exists = await db.QuoteMarketSchemas
            .AnyAsync(s => s.MarketCode == schema.MarketCode && s.Version == schema.Version, ct);
        if (exists)
        {
            return;
        }

        db.QuoteMarketSchemas.Add(schema);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Another seeder process won the race and inserted the same PK first.
            // Detach and continue — the desired terminal state ("row exists") is achieved.
            db.Entry(schema).State = EntityState.Detached;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg
        && pg.SqlState == PostgresErrorCodes.UniqueViolation;

    /// <summary>
    /// Builds a schema row using only the entity defaults (validity_days=14,
    /// rate_limit_per_customer_per_hour=10, rate_limit_per_company_per_hour=50,
    /// company_verification_required=false, tax_preview_drift_threshold_pct=5.00,
    /// sla_decision_business_days=2, sla_warning_business_days=1, invitation_ttl_days=14,
    /// holidays_list=[]). Per-market overrides land here when the market-team finalizes
    /// holiday calendars + SLA targets (Phase 3+ task).
    /// </summary>
    private static QuoteMarketSchema BuildSchema(string marketCode, int version, DateTimeOffset effectiveFrom) => new()
    {
        MarketCode = marketCode,
        Version = version,
        EffectiveFrom = effectiveFrom,
        EffectiveTo = null,
        // All other fields ride the entity defaults declared in QuoteMarketSchema.cs.
    };
}
