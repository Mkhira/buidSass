using System.Text.Json;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BackendApi.Modules.Verification.Seeding;

/// <summary>
/// Spec 020 task T113. Dev-only synthetic dataset that exercises every state
/// of the Verification state machine, plus sample documents and reminders.
/// Supports demos and manual QA without forcing the operator to drive
/// transitions by hand.
///
/// <para>Hard-gated: <see cref="SeedGuard"/> blocks Production; this seeder
/// also short-circuits if the host environment isn't Development. Idempotent
/// — re-runs are no-ops once the synthetic rows exist.</para>
///
/// <para>Customers seeded:</para>
/// <list type="bullet">
///   <item><c>11111111-...-001</c> — submitted (KSA, dentist).</item>
///   <item><c>11111111-...-002</c> — in-review (KSA, dental_lab_tech).</item>
///   <item><c>11111111-...-003</c> — info-requested (KSA, dental_student).</item>
///   <item><c>11111111-...-004</c> — approved, near-expiry (KSA, dentist).</item>
///   <item><c>11111111-...-005</c> — rejected with active cooldown (KSA, dentist).</item>
///   <item><c>11111111-...-006</c> — expired (KSA, dentist).</item>
///   <item><c>11111111-...-007</c> — revoked (KSA, dentist).</item>
///   <item><c>11111111-...-008</c> — superseded → renewal approved (KSA, dentist).</item>
///   <item><c>11111111-...-009</c> — voided via account-locked (EG, dentist).</item>
///   <item><c>11111111-...-010</c> — approved, mid-life (EG, clinic_buyer).</item>
/// </list>
/// </summary>
public sealed class VerificationDevDataSeeder : ISeeder
{
    public string Name => "verification.dev-data";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => ["verification.reference-data"];

    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        if (!ctx.Env.IsDevelopment())
        {
            return;
        }

        var db = ctx.Services.GetRequiredService<VerificationDbContext>();

        var ksaSchema = await db.MarketSchemas.AsNoTracking()
            .Where(s => s.MarketCode == "ksa" && s.EffectiveTo == null)
            .OrderByDescending(s => s.Version)
            .SingleOrDefaultAsync(ct);
        var egSchema = await db.MarketSchemas.AsNoTracking()
            .Where(s => s.MarketCode == "eg" && s.EffectiveTo == null)
            .OrderByDescending(s => s.Version)
            .SingleOrDefaultAsync(ct);
        if (ksaSchema is null || egSchema is null)
        {
            // Reference data not seeded yet — bail without partial state.
            return;
        }

        var seeds = BuildSyntheticRows(ksaSchema, egSchema);

        // Idempotency — find rows already seeded by id.
        var existingIds = await db.Verifications.AsNoTracking()
            .Where(v => seeds.Select(s => s.Verification.Id).Contains(v.Id))
            .Select(v => v.Id)
            .ToListAsync(ct);

        var existingSet = existingIds.ToHashSet();
        foreach (var seed in seeds)
        {
            if (existingSet.Contains(seed.Verification.Id))
            {
                continue;
            }
            db.Verifications.Add(seed.Verification);
            foreach (var transition in seed.Transitions)
            {
                db.StateTransitions.Add(transition);
            }
            foreach (var document in seed.Documents)
            {
                db.Documents.Add(document);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static IReadOnlyList<SeedRow> BuildSyntheticRows(
        VerificationMarketSchema ksaSchema,
        VerificationMarketSchema egSchema)
    {
        var rows = new List<SeedRow>();

        // 001 — submitted
        rows.Add(BuildRow(
            customerId: Guid.Parse("11111111-1111-1111-1111-111111111001"),
            verificationId: Guid.Parse("22222222-1111-1111-1111-111111111001"),
            schema: ksaSchema,
            profession: "dentist",
            regulator: "SCFHS-1234567",
            state: VerificationState.Submitted,
            submittedAt: BaseTime,
            decidedAt: null,
            expiresAt: null,
            extra: row =>
            {
                row.Transitions.Add(BuildInitialTransition(row.Verification, BaseTime));
            }));

        // 002 — in-review
        rows.Add(BuildRow(
            customerId: Guid.Parse("11111111-1111-1111-1111-111111111002"),
            verificationId: Guid.Parse("22222222-1111-1111-1111-111111111002"),
            schema: ksaSchema,
            profession: "dental_lab_tech",
            regulator: "SCFHS-2345678",
            state: VerificationState.InReview,
            submittedAt: BaseTime,
            decidedAt: null,
            expiresAt: null,
            extra: row =>
            {
                row.Transitions.Add(BuildInitialTransition(row.Verification, BaseTime));
                row.Transitions.Add(BuildTransition(row.Verification.Id, row.Verification.MarketCode,
                    VerificationState.Submitted, VerificationState.InReview, VerificationActorKind.Reviewer,
                    actorId: Guid.NewGuid(), reason: "reviewer_picked_up", BaseTime.AddHours(2)));
            }));

        // 003 — info-requested
        rows.Add(BuildRow(
            customerId: Guid.Parse("11111111-1111-1111-1111-111111111003"),
            verificationId: Guid.Parse("22222222-1111-1111-1111-111111111003"),
            schema: ksaSchema,
            profession: "dental_student",
            regulator: "SCFHS-3456789",
            state: VerificationState.InfoRequested,
            submittedAt: BaseTime,
            decidedAt: null,
            expiresAt: null,
            extra: row =>
            {
                row.Transitions.Add(BuildInitialTransition(row.Verification, BaseTime));
                row.Transitions.Add(BuildTransition(row.Verification.Id, row.Verification.MarketCode,
                    VerificationState.Submitted, VerificationState.InfoRequested, VerificationActorKind.Reviewer,
                    actorId: Guid.NewGuid(), reason: "reviewer_request_info", BaseTime.AddHours(3)));
            }));

        // 004 — approved, near-expiry (28 days from base time)
        var nearExpiryApprovedAt = BaseTime.AddDays(-337); // 365-28
        rows.Add(BuildRow(
            customerId: Guid.Parse("11111111-1111-1111-1111-111111111004"),
            verificationId: Guid.Parse("22222222-1111-1111-1111-111111111004"),
            schema: ksaSchema,
            profession: "dentist",
            regulator: "SCFHS-4567890",
            state: VerificationState.Approved,
            submittedAt: nearExpiryApprovedAt,
            decidedAt: nearExpiryApprovedAt.AddHours(2),
            expiresAt: nearExpiryApprovedAt.AddDays(365),
            extra: row =>
            {
                row.Transitions.Add(BuildInitialTransition(row.Verification, nearExpiryApprovedAt));
                row.Transitions.Add(BuildTransition(row.Verification.Id, row.Verification.MarketCode,
                    VerificationState.Submitted, VerificationState.Approved, VerificationActorKind.Reviewer,
                    actorId: Guid.NewGuid(), reason: "reviewer_approve", nearExpiryApprovedAt.AddHours(2)));
            }));

        // 005 — rejected with active cooldown
        rows.Add(BuildRow(
            customerId: Guid.Parse("11111111-1111-1111-1111-111111111005"),
            verificationId: Guid.Parse("22222222-1111-1111-1111-111111111005"),
            schema: ksaSchema,
            profession: "dentist",
            regulator: "SCFHS-5678901",
            state: VerificationState.Rejected,
            submittedAt: BaseTime.AddDays(-2),
            decidedAt: BaseTime.AddDays(-2).AddHours(4),
            expiresAt: null,
            extra: row =>
            {
                var rejectedAt = BaseTime.AddDays(-2).AddHours(4);
                row.Transitions.Add(BuildInitialTransition(row.Verification, BaseTime.AddDays(-2)));
                row.Transitions.Add(BuildTransition(row.Verification.Id, row.Verification.MarketCode,
                    VerificationState.Submitted, VerificationState.Rejected, VerificationActorKind.Reviewer,
                    actorId: Guid.NewGuid(), reason: "reviewer_reject",
                    rejectedAt,
                    metadata: new Dictionary<string, object?>
                    {
                        ["cooldown_until"] = rejectedAt.AddDays(7),
                    }));
            }));

        // 006 — expired
        rows.Add(BuildRow(
            customerId: Guid.Parse("11111111-1111-1111-1111-111111111006"),
            verificationId: Guid.Parse("22222222-1111-1111-1111-111111111006"),
            schema: ksaSchema,
            profession: "dentist",
            regulator: "SCFHS-6789012",
            state: VerificationState.Expired,
            submittedAt: BaseTime.AddDays(-400),
            decidedAt: BaseTime.AddDays(-400).AddHours(2),
            expiresAt: BaseTime.AddDays(-35),
            extra: row =>
            {
                var approvedAt = BaseTime.AddDays(-400).AddHours(2);
                row.Transitions.Add(BuildInitialTransition(row.Verification, BaseTime.AddDays(-400)));
                row.Transitions.Add(BuildTransition(row.Verification.Id, row.Verification.MarketCode,
                    VerificationState.Submitted, VerificationState.Approved, VerificationActorKind.Reviewer,
                    actorId: Guid.NewGuid(), reason: "reviewer_approve", approvedAt));
                row.Transitions.Add(BuildTransition(row.Verification.Id, row.Verification.MarketCode,
                    VerificationState.Approved, VerificationState.Expired, VerificationActorKind.System,
                    actorId: null, reason: "verification_expired", BaseTime.AddDays(-35)));
            }));

        // 007 — revoked
        rows.Add(BuildRow(
            customerId: Guid.Parse("11111111-1111-1111-1111-111111111007"),
            verificationId: Guid.Parse("22222222-1111-1111-1111-111111111007"),
            schema: ksaSchema,
            profession: "dentist",
            regulator: "SCFHS-7890123",
            state: VerificationState.Revoked,
            submittedAt: BaseTime.AddDays(-90),
            decidedAt: BaseTime.AddDays(-90).AddHours(2),
            expiresAt: BaseTime.AddDays(-90).AddDays(365),
            extra: row =>
            {
                var approvedAt = BaseTime.AddDays(-90).AddHours(2);
                var revokedAt = BaseTime.AddDays(-5);
                row.Transitions.Add(BuildInitialTransition(row.Verification, BaseTime.AddDays(-90)));
                row.Transitions.Add(BuildTransition(row.Verification.Id, row.Verification.MarketCode,
                    VerificationState.Submitted, VerificationState.Approved, VerificationActorKind.Reviewer,
                    actorId: Guid.NewGuid(), reason: "reviewer_approve", approvedAt));
                row.Transitions.Add(BuildTransition(row.Verification.Id, row.Verification.MarketCode,
                    VerificationState.Approved, VerificationState.Revoked, VerificationActorKind.Reviewer,
                    actorId: Guid.NewGuid(), reason: "reviewer_revoke", revokedAt));
            }));

        // 008 — superseded by renewal
        var supersededId = Guid.Parse("22222222-1111-1111-1111-111111111008");
        var renewalId = Guid.Parse("22222222-1111-1111-1111-111111118888");
        rows.Add(BuildRow(
            customerId: Guid.Parse("11111111-1111-1111-1111-111111111008"),
            verificationId: supersededId,
            schema: ksaSchema,
            profession: "dentist",
            regulator: "SCFHS-8901234",
            state: VerificationState.Superseded,
            submittedAt: BaseTime.AddDays(-360),
            decidedAt: BaseTime.AddDays(-360).AddHours(2),
            expiresAt: BaseTime.AddDays(-360).AddDays(365),
            supersededById: renewalId,
            extra: row =>
            {
                var approvedAt = BaseTime.AddDays(-360).AddHours(2);
                row.Transitions.Add(BuildInitialTransition(row.Verification, BaseTime.AddDays(-360)));
                row.Transitions.Add(BuildTransition(row.Verification.Id, row.Verification.MarketCode,
                    VerificationState.Submitted, VerificationState.Approved, VerificationActorKind.Reviewer,
                    actorId: Guid.NewGuid(), reason: "reviewer_approve", approvedAt));
                row.Transitions.Add(BuildTransition(row.Verification.Id, row.Verification.MarketCode,
                    VerificationState.Approved, VerificationState.Superseded, VerificationActorKind.System,
                    actorId: null, reason: "renewal_approved", BaseTime.AddDays(-1),
                    metadata: new Dictionary<string, object?> { ["renewal_id"] = renewalId }));
            }));
        // 008b — the renewal itself, currently approved
        rows.Add(BuildRow(
            customerId: Guid.Parse("11111111-1111-1111-1111-111111111008"),
            verificationId: renewalId,
            schema: ksaSchema,
            profession: "dentist",
            regulator: "SCFHS-8901234",
            state: VerificationState.Approved,
            submittedAt: BaseTime.AddDays(-2),
            decidedAt: BaseTime.AddDays(-1),
            expiresAt: BaseTime.AddDays(-1).AddDays(365),
            supersedesId: supersededId,
            extra: row =>
            {
                row.Transitions.Add(BuildInitialTransition(row.Verification, BaseTime.AddDays(-2)));
                row.Transitions.Add(BuildTransition(row.Verification.Id, row.Verification.MarketCode,
                    VerificationState.Submitted, VerificationState.Approved, VerificationActorKind.Reviewer,
                    actorId: Guid.NewGuid(), reason: "reviewer_approve", BaseTime.AddDays(-1)));
            }));

        // 009 — voided via account lifecycle (EG market)
        rows.Add(BuildRow(
            customerId: Guid.Parse("11111111-1111-1111-1111-111111111009"),
            verificationId: Guid.Parse("22222222-1111-1111-1111-111111111009"),
            schema: egSchema,
            profession: "dentist",
            regulator: "EMS/12345/678",
            state: VerificationState.Void,
            submittedAt: BaseTime.AddDays(-10),
            decidedAt: null,
            expiresAt: null,
            extra: row =>
            {
                row.Verification.VoidReason = "account_inactive";
                row.Transitions.Add(BuildInitialTransition(row.Verification, BaseTime.AddDays(-10)));
                row.Transitions.Add(BuildTransition(row.Verification.Id, row.Verification.MarketCode,
                    VerificationState.Submitted, VerificationState.Void, VerificationActorKind.System,
                    actorId: null, reason: "account_inactive", BaseTime.AddDays(-3)));
            }));

        // 010 — approved mid-life (EG)
        rows.Add(BuildRow(
            customerId: Guid.Parse("11111111-1111-1111-1111-111111111010"),
            verificationId: Guid.Parse("22222222-1111-1111-1111-111111111010"),
            schema: egSchema,
            profession: "clinic_buyer",
            regulator: "EMS/98765/432",
            state: VerificationState.Approved,
            submittedAt: BaseTime.AddDays(-180),
            decidedAt: BaseTime.AddDays(-180).AddHours(3),
            expiresAt: BaseTime.AddDays(-180).AddDays(365),
            extra: row =>
            {
                var approvedAt = BaseTime.AddDays(-180).AddHours(3);
                row.Transitions.Add(BuildInitialTransition(row.Verification, BaseTime.AddDays(-180)));
                row.Transitions.Add(BuildTransition(row.Verification.Id, row.Verification.MarketCode,
                    VerificationState.Submitted, VerificationState.Approved, VerificationActorKind.Reviewer,
                    actorId: Guid.NewGuid(), reason: "reviewer_approve", approvedAt));
            }));

        return rows;
    }

    private static SeedRow BuildRow(
        Guid customerId,
        Guid verificationId,
        VerificationMarketSchema schema,
        string profession,
        string regulator,
        VerificationState state,
        DateTimeOffset submittedAt,
        DateTimeOffset? decidedAt,
        DateTimeOffset? expiresAt,
        Action<SeedRow> extra,
        Guid? supersedesId = null,
        Guid? supersededById = null)
    {
        var verification = new Entities.Verification
        {
            Id = verificationId,
            CustomerId = customerId,
            MarketCode = schema.MarketCode,
            SchemaVersion = schema.Version,
            Profession = profession,
            RegulatorIdentifier = regulator,
            State = state,
            SubmittedAt = submittedAt,
            DecidedAt = decidedAt,
            DecidedBy = decidedAt is null ? null : Guid.NewGuid(),
            ExpiresAt = expiresAt,
            SupersedesId = supersedesId,
            SupersededById = supersededById,
            RestrictionPolicySnapshotJson = "{}",
            CreatedAt = submittedAt,
            UpdatedAt = decidedAt ?? submittedAt,
        };
        var seed = new SeedRow(verification, new List<VerificationStateTransition>(), new List<VerificationDocument>());
        extra(seed);
        return seed;
    }

    private static VerificationStateTransition BuildInitialTransition(Entities.Verification verification, DateTimeOffset at)
        => new()
        {
            Id = Guid.NewGuid(),
            VerificationId = verification.Id,
            MarketCode = verification.MarketCode,
            PriorState = VerificationStateMachine.PriorStateNoneWire,
            NewState = VerificationState.Submitted.ToWireValue(),
            ActorKind = VerificationActorKind.Customer.ToWireValue(),
            ActorId = verification.CustomerId,
            Reason = "customer_submission",
            MetadataJson = "{}",
            OccurredAt = at,
        };

    private static VerificationStateTransition BuildTransition(
        Guid verificationId,
        string marketCode,
        VerificationState prior,
        VerificationState next,
        VerificationActorKind actorKind,
        Guid? actorId,
        string reason,
        DateTimeOffset at,
        Dictionary<string, object?>? metadata = null)
        => new()
        {
            Id = Guid.NewGuid(),
            VerificationId = verificationId,
            MarketCode = marketCode,
            PriorState = prior.ToWireValue(),
            NewState = next.ToWireValue(),
            ActorKind = actorKind.ToWireValue(),
            ActorId = actorId,
            Reason = reason,
            MetadataJson = metadata is null ? "{}" : JsonSerializer.Serialize(metadata),
            OccurredAt = at,
        };

    private sealed record SeedRow(
        Entities.Verification Verification,
        List<VerificationStateTransition> Transitions,
        List<VerificationDocument> Documents);
}
