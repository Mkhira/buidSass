using BackendApi.Features.Seeding;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BackendApi.Modules.Verification.Seeding;

/// <summary>
/// Reference-data seeder per spec 020 quickstart §2 / tasks T040.
/// Idempotent INSERT of the KSA + EG verification market schemas (version 1).
/// Re-running the seeder is a no-op once the rows exist; schema changes ship as
/// a new <see cref="VerificationMarketSchema.Version"/> via a follow-up seeder
/// or migration.
/// </summary>
public sealed class VerificationReferenceDataSeeder : ISeeder
{
    public string Name => "verification.reference-data";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => [];

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        var db = ctx.Services.GetRequiredService<VerificationDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;

        // Conflict-tolerant insert per row. The check-then-add pattern races under
        // concurrent seeder runs (two nodes both pass AnyAsync, both call Add, the
        // second SaveChanges fails on the (MarketCode, Version) PK). Inserting one
        // row at a time and swallowing the unique-violation lets concurrent runs
        // converge to a clean no-op without sacrificing isolation.
        await TryInsertAsync(db, BuildSchema(
            marketCode: "ksa",
            version: 1,
            effectiveFrom: nowUtc,
            retentionMonths: 24,
            requiredFieldsJson: KsaRequiredFieldsJson), ct);

        await TryInsertAsync(db, BuildSchema(
            marketCode: "eg",
            version: 1,
            effectiveFrom: nowUtc,
            retentionMonths: 36,
            requiredFieldsJson: EgRequiredFieldsJson), ct);
    }

    private static async Task TryInsertAsync(
        VerificationDbContext db,
        VerificationMarketSchema schema,
        CancellationToken ct)
    {
        // Cheap pre-check first — the common path (already seeded) avoids an
        // exception and keeps logs quiet.
        var exists = await db.MarketSchemas
            .AnyAsync(s => s.MarketCode == schema.MarketCode && s.Version == schema.Version, ct);
        if (exists)
        {
            return;
        }

        db.MarketSchemas.Add(schema);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Another seeder process won the race and inserted the same PK first.
            // Detach the entity so this DbContext stays clean, then continue —
            // the desired terminal state ("row exists") is achieved.
            db.Entry(schema).State = EntityState.Detached;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg
        && pg.SqlState == PostgresErrorCodes.UniqueViolation;

    private static VerificationMarketSchema BuildSchema(
        string marketCode,
        int version,
        DateTimeOffset effectiveFrom,
        int retentionMonths,
        string requiredFieldsJson) => new()
        {
            MarketCode = marketCode,
            Version = version,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = null,
            RequiredFieldsJson = requiredFieldsJson,
            // Defaults match the entity defaults (allowed types / reminder windows / SLA / holidays).
            RetentionMonths = retentionMonths,
            CooldownDays = 7,
            ExpiryDays = 365,
            SlaDecisionBusinessDays = 2,
            SlaWarningBusinessDays = 1,
        };

    /// <summary>
    /// KSA schema v1 — SCFHS license format (`SCFHS-` prefix + 7-digit number is
    /// representative; the actual regulator format is captured in the regex per
    /// data-model §2.4 sample).
    /// </summary>
    private const string KsaRequiredFieldsJson = """
[
  {
    "name": "profession",
    "kind": "enum",
    "required": true,
    "pattern": null,
    "enumValues": ["dentist", "dental_lab_tech", "dental_student", "clinic_buyer"],
    "labelKeyEn": "verification.field.profession.label",
    "labelKeyAr": "verification.field.profession.label"
  },
  {
    "name": "regulator_identifier",
    "kind": "text",
    "required": true,
    "pattern": "^[A-Z0-9-]{6,20}$",
    "enumValues": null,
    "labelKeyEn": "verification.field.regulator_identifier.ksa.label",
    "labelKeyAr": "verification.field.regulator_identifier.ksa.label"
  }
]
""";

    /// <summary>
    /// EG schema v1 — Egyptian Medical Syndicate registration. Same fields as
    /// KSA at V1; the difference is captured in the per-market label key.
    /// </summary>
    private const string EgRequiredFieldsJson = """
[
  {
    "name": "profession",
    "kind": "enum",
    "required": true,
    "pattern": null,
    "enumValues": ["dentist", "dental_lab_tech", "dental_student", "clinic_buyer"],
    "labelKeyEn": "verification.field.profession.label",
    "labelKeyAr": "verification.field.profession.label"
  },
  {
    "name": "regulator_identifier",
    "kind": "text",
    "required": true,
    "pattern": "^[A-Z0-9/-]{6,20}$",
    "enumValues": null,
    "labelKeyEn": "verification.field.regulator_identifier.eg.label",
    "labelKeyAr": "verification.field.regulator_identifier.eg.label"
  }
]
""";
}
