using System.Text.Json;
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
    public Task<SubmitResult> HandleAsync(
        Guid customerId,
        string marketCode,
        SubmitVerificationRequest request,
        CancellationToken ct)
        => HandleAsync(customerId, marketCode, request, idempotencyKey: null, ct);

    public async Task<SubmitResult> HandleAsync(
        Guid customerId,
        string marketCode,
        SubmitVerificationRequest request,
        string? idempotencyKey,
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

        // 2. Validate SupersedesId before any guard bypass. A non-null
        //    SupersedesId is the renewal entry-point (data-model §3.2 renewal
        //    exception), but ONLY when it points to a row that:
        //      (a) exists,
        //      (b) belongs to the SAME customer (else the request is forging
        //          a renewal of someone else's approval — security boundary),
        //      (c) is currently in `approved` state (a renewal of a rejected /
        //          expired / revoked / superseded / void row is not a legitimate
        //          renewal — the customer must submit fresh),
        //      (d) is in the SAME market as this submission (cross-market
        //          renewals are out of scope V1; markets are independently
        //          regulated).
        //    Without all four, we fall through to the AlreadyPending check
        //    (treated as a fresh submission) — and reject with
        //    RenewalNotEligible so the caller knows the bypass attempt failed.
        Entities.Verification? priorApproval = null;
        if (request.SupersedesId is { } supersedesId)
        {
            priorApproval = await db.Verifications
                .AsNoTracking()
                .SingleOrDefaultAsync(v => v.Id == supersedesId, ct);

            if (priorApproval is null
                || priorApproval.CustomerId != customerId
                || priorApproval.State != VerificationState.Approved
                || priorApproval.MarketCode != schema.MarketCode)
            {
                return SubmitResult.Fail(
                    VerificationReasonCode.RenewalNotEligible,
                    "supersedes_id does not reference an active approval owned by this customer in this market.");
            }
        }

        // 3. In-flight guard (per data-model §3.2). Two modes:
        //
        //    Fresh submission (priorApproval is null): block if the customer
        //    already has any non-terminal verification.
        //
        //    Validated renewal (priorApproval non-null): the prior approval is
        //    expected to be in flight, so it doesn't count. But we MUST still
        //    block a duplicate pending renewal — i.e., another non-terminal
        //    row for this customer that isn't the prior approval itself. Without
        //    this check, a direct SubmitVerification with SupersedesId could
        //    bypass renewal policy and create stacked pending renewals (which
        //    RequestRenewalHandler is supposed to police).
        if (priorApproval is null)
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
        else
        {
            // Renewal mode: the prior approval is excluded from the in-flight set
            // (it's in Approved which is non-terminal). Anything else non-terminal
            // is a duplicate pending renewal — reject.
            var priorApprovalId = priorApproval.Id;
            var hasOpenRenewal = await db.Verifications.AnyAsync(
                v => v.CustomerId == customerId
                  && v.Id != priorApprovalId
                  && v.State != VerificationState.Approved
                  && v.State != VerificationState.Rejected
                  && v.State != VerificationState.Expired
                  && v.State != VerificationState.Revoked
                  && v.State != VerificationState.Superseded
                  && v.State != VerificationState.Void,
                ct);

            if (hasOpenRenewal)
            {
                return SubmitResult.Fail(
                    VerificationReasonCode.AlreadyPending,
                    "A non-terminal renewal already exists for this customer.");
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
            MarketCode = schema.MarketCode,
            PriorState = VerificationStateMachine.PriorStateNoneWire,
            NewState = VerificationState.Submitted.ToWireValue(),
            ActorKind = VerificationActorKind.Customer.ToWireValue(),
            ActorId = customerId,
            Reason = "customer_submission",
            MetadataJson = SerializeSubmissionMetadata(request, idempotencyKey),
            OccurredAt = nowUtc,
        };

        db.Verifications.Add(verification);
        db.StateTransitions.Add(initialTransition);

        // 4. Rebuild eligibility cache inside the same DbContext (Phase 2 stub no-ops
        //    until Phase 5 fills in the read-and-UPSERT path).
        await eligibilityInvalidator.RebuildAsync(customerId, schema.MarketCode, db, ct);

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

    private static string SerializeSubmissionMetadata(SubmitVerificationRequest request, string? idempotencyKey)
    {
        // Light-weight metadata sidecar; full document linkage lands with the
        // AttachDocument slice once the storage path is wired. idempotency_key
        // is captured for audit-trail traceability — full distributed dedup
        // ships with the platform IdempotencyStore in a follow-up (per Checkout's
        // pattern). Use JsonSerializer (not string concat) so customer-controlled
        // header values containing quotes / backslashes / control chars produce
        // valid jsonb (Principle 25 — audit trail traceability).
        return JsonSerializer.Serialize(new
        {
            document_ids = request.DocumentIds,
            supersedes_id = request.SupersedesId,
            idempotency_key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
        });
    }
}

public sealed record SubmitResult(bool IsSuccess, SubmitVerificationResponse? Response, VerificationReasonCode? ReasonCode, string? Detail)
{
    public static SubmitResult Ok(SubmitVerificationResponse response) =>
        new(true, response, null, null);

    public static SubmitResult Fail(VerificationReasonCode code, string detail) =>
        new(false, null, code, detail);
}
