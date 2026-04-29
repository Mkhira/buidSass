using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Verification.Eligibility;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Verification.Customer.SubmitVerification;

/// <summary>
/// Spec 020 contracts §2.1 / tasks T052. Transactional submission handler:
/// <list type="number">
///   <item>resolve the active per-market schema (CHECK guards lowercase market code);</item>
///   <item>guard against another non-terminal verification for this customer (unless renewal);</item>
///   <item>INSERT <see cref="Entities.Verification"/> in <see cref="VerificationState.Submitted"/>;</item>
///   <item>INSERT the initial <see cref="VerificationStateTransition"/> ledger row (PriorState=__none__);</item>
///   <item>rebuild the eligibility cache (Phase 2 stub; Phase 5 fills in the real logic);</item>
///   <item>publish an audit event via <see cref="IAuditEventPublisher"/>.</item>
/// </list>
/// All within a single SaveChanges call so the row + transition land atomically.
/// </summary>
public sealed class SubmitVerificationHandler(
    VerificationDbContext db,
    EligibilityCacheInvalidator eligibilityInvalidator,
    IAuditEventPublisher auditPublisher,
    TimeProvider clock,
    ILogger<SubmitVerificationHandler> logger)
{
    public async Task<SubmitResult> HandleAsync(
        Guid customerId,
        string marketCode,
        SubmitVerificationRequest request,
        CancellationToken ct)
    {
        var nowUtc = clock.GetUtcNow();

        // 1. Active schema for the customer's market.
        var schema = await db.MarketSchemas
            .Where(s => s.MarketCode == marketCode && s.EffectiveTo == null)
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync(ct);

        if (schema is null)
        {
            return SubmitResult.Fail(
                VerificationReasonCode.MarketUnsupported,
                $"No active verification schema is configured for market '{marketCode}'.");
        }

        // 2. No-other-non-terminal guard (per data-model §3.2). Renewals (with
        //    SupersedesId set) are exempt — the renewal goes through the normal
        //    flow while the prior approval remains active.
        if (request.SupersedesId is null)
        {
            var hasOpen = await db.Verifications.AnyAsync(
                v => v.CustomerId == customerId
                  && v.State != VerificationState.Rejected
                  && v.State != VerificationState.Expired
                  && v.State != VerificationState.Revoked
                  && v.State != VerificationState.Superseded
                  && v.State != VerificationState.Void,
                ct);

            if (hasOpen)
            {
                return SubmitResult.Fail(
                    VerificationReasonCode.AlreadyPending,
                    "Customer already has a non-terminal verification in flight.");
            }
        }

        // 3. INSERT the verification + initial state-transition ledger row.
        var verificationId = Guid.NewGuid();
        var verification = new Entities.Verification
        {
            Id = verificationId,
            CustomerId = customerId,
            MarketCode = schema.MarketCode,
            SchemaVersion = schema.Version,
            Profession = request.Profession,
            RegulatorIdentifier = request.RegulatorIdentifier,
            State = VerificationState.Submitted,
            SubmittedAt = nowUtc,
            SupersedesId = request.SupersedesId,
            // Phase 5 will populate this from IProductRestrictionPolicy at the right
            // chokepoint. Phase 3 ships an empty snapshot so the audit-replay shape
            // is stable.
            RestrictionPolicySnapshotJson = "{}",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };

        var initialTransition = new VerificationStateTransition
        {
            Id = Guid.NewGuid(),
            VerificationId = verificationId,
            PriorState = VerificationStateMachine.PriorStateNoneWire,
            NewState = VerificationState.Submitted.ToWireValue(),
            ActorKind = VerificationActorKind.Customer.ToWireValue(),
            ActorId = customerId,
            Reason = "customer_submission",
            MetadataJson = SerializeSubmissionMetadata(request),
            OccurredAt = nowUtc,
        };

        db.Verifications.Add(verification);
        db.StateTransitions.Add(initialTransition);

        // 4. Rebuild eligibility cache inside the same DbContext (Phase 2 stub no-ops
        //    until Phase 5 fills in the read-and-UPSERT path).
        await eligibilityInvalidator.RebuildAsync(customerId, db, ct);

        await db.SaveChangesAsync(ct);

        // 5. Audit. Failure to publish does not roll back the submission per FR-034
        //    style — log and proceed; subscriber catch-up runs out of band.
        try
        {
            await auditPublisher.PublishAsync(new AuditEvent(
                    ActorId: customerId,
                    ActorRole: "customer",
                    Action: "verification.state_changed",
                    EntityType: "verification",
                    EntityId: verificationId,
                    BeforeState: null,
                    AfterState: new
                    {
                        prior_state = VerificationStateMachine.PriorStateNoneWire,
                        new_state = VerificationState.Submitted.ToWireValue(),
                        market_code = schema.MarketCode,
                        schema_version = schema.Version,
                        supersedes_id = request.SupersedesId,
                    },
                    Reason: "customer_submission"),
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Verification {VerificationId} submitted but audit publish failed; subscriber catch-up will reconcile.",
                verificationId);
        }

        return SubmitResult.Ok(new SubmitVerificationResponse(
            Id: verificationId,
            MarketCode: schema.MarketCode,
            SchemaVersion: schema.Version,
            State: VerificationState.Submitted.ToWireValue(),
            SubmittedAt: nowUtc,
            SupersedesId: request.SupersedesId));
    }

    private static string SerializeSubmissionMetadata(SubmitVerificationRequest request)
    {
        // Light-weight metadata sidecar; full document linkage lands with the
        // AttachDocument slice (T054) once the storage path is wired.
        var docs = string.Join(",", request.DocumentIds.Select(d => $"\"{d}\""));
        var supersedes = request.SupersedesId is { } sid ? $"\"{sid}\"" : "null";
        return $"{{\"document_ids\":[{docs}],\"supersedes_id\":{supersedes}}}";
    }
}

public sealed record SubmitResult(bool IsSuccess, SubmitVerificationResponse? Response, VerificationReasonCode? ReasonCode, string? Detail)
{
    public static SubmitResult Ok(SubmitVerificationResponse response) =>
        new(true, response, null, null);

    public static SubmitResult Fail(VerificationReasonCode code, string detail) =>
        new(false, null, code, detail);
}
