using BackendApi.Features.Seeding;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BackendApi.Modules.Support.Seeding;

/// <summary>
/// Reference-data seeder per spec 023 tasks T045 / data-model §4.
/// Idempotent insert of the KSA + EG market-schema rows + 8 SLA-policy rows
/// (4 priorities × 2 markets) with FR-021 defaults.
/// </summary>
public sealed class SupportReferenceDataSeeder : ISeeder
{
    public string Name => "support.reference-data";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    private static readonly string[] Markets = { "SA", "EG" };

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        var db = ctx.Services.GetRequiredService<SupportDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;
        Guid? systemActor = null;

        foreach (var market in Markets)
        {
            await UpsertMarketSchemaAsync(db, BuildDefaultSchema(market, systemActor, nowUtc), ct);

            foreach (var priority in TicketPriorityNames.All)
            {
                var (firstResponse, resolution) = SlaPolicySnapshot.DefaultFor(priority);
                await UpsertSlaPolicyAsync(db, new SlaPolicy
                {
                    MarketCode = market,
                    Priority = priority,
                    FirstResponseTargetMinutes = firstResponse,
                    ResolutionTargetMinutes = resolution,
                    UpdatedAtUtc = nowUtc,
                    UpdatedByActorId = systemActor,
                }, ct);
            }
        }
    }

    private static SupportMarketSchema BuildDefaultSchema(string marketCode, Guid? actor, DateTimeOffset nowUtc) => new()
    {
        MarketCode = marketCode,
        AutoAssignmentEnabled = false,
        ReopenWindowDays = 14,
        MaxReopenCount = 3,
        AutoCloseAfterResolvedDays = 7,
        AttachmentMaxPerTicket = 10,
        AttachmentMaxSizeMb = 10,
        AttachmentCumulativeMaxMb = 50,
        AllowedMimeTypes = SupportMarketPolicy.DefaultAllowedMimeTypes,
        UpdatedAtUtc = nowUtc,
        UpdatedByActorId = actor,
    };

    private static async Task UpsertMarketSchemaAsync(SupportDbContext db, SupportMarketSchema schema, CancellationToken ct)
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

    private static async Task UpsertSlaPolicyAsync(SupportDbContext db, SlaPolicy policy, CancellationToken ct)
    {
        var exists = await db.SlaPolicies.AnyAsync(
            p => p.MarketCode == policy.MarketCode && p.Priority == policy.Priority, ct);
        if (exists) return;

        db.SlaPolicies.Add(policy);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.Entry(policy).State = EntityState.Detached;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == "23505";
}
