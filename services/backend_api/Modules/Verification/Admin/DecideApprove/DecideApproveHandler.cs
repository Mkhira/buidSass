using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Eligibility;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Verification.Admin.DecideApprove;

/// <summary>
/// Spec 020 contracts §3.3 / tasks T072. Transactional reviewer approval:
/// <list type="number">
///   <item>load row with xmin guard;</item>
///   <item>state-machine guard via <see cref="VerificationStateMachine.EnsureCanTransitionOrThrow"/>;</item>
///   <item>resolve schema by snapshotted version → compute <c>expires_at</c>;</item>
///   <item>if <c>SupersedesId</c> not null, load prior approval and transition it to
///         <c>superseded</c> in the same Tx (writes its own state-transition row);</item>
///   <item>flip approving row to <see cref="VerificationState.Approved"/>;</item>
///   <item>append the <see cref="VerificationStateTransition"/> ledger row;</item>
///   <item>rebuild eligibility cache via <see cref="EligibilityCacheInvalidator"/>;</item>
///   <item>SaveChangesAsync — xmin mismatch surfaces as <see cref="DbUpdateConcurrencyException"/>
///         and maps to <see cref="VerificationReasonCode.AlreadyDecided"/>;</item>
///   <item>publish audit event.</item>
/// </list>
/// </summary>
public sealed class DecideApproveHandler(
    VerificationDbContext db,
    EligibilityCacheInvalidator eligibilityInvalidator,
    IAuditEventPublisher auditPublisher,
    IVerificationDomainEventPublisher domainPublisher,
    TimeProvider clock,
    ILogger<DecideApproveHandler> logger)
{
    public Task<DecideApproveResult> HandleAsync(
        Guid verificationId,
        Guid reviewerId,
        DecideApproveRequest request,
        CancellationToken ct)
        => HandleAsync(verificationId, reviewerId, reviewerMarkets: null, request, ct);

    public async Task<DecideApproveResult> HandleAsync(
        Guid verificationId,
        Guid reviewerId,
        IReadOnlySet<string>? reviewerMarkets,
        DecideApproveRequest request,
        CancellationToken ct)
    {
        var nowUtc = clock.GetUtcNow();

        var verification = await db.Verifications
            .SingleOrDefaultAsync(v => v.Id == verificationId, ct);

        if (verification is null)
        {
            return DecideApproveResult.Fail(
                VerificationReasonCode.InvalidStateForAction,
                "Verification not found.");
        }

        // Reviewer market scope (security boundary): a KSA reviewer must not
        // be able to act on an EG row. Use the same "404-style not-found"
        // signal that GetVerificationDetail uses to avoid leaking row
        // existence to a foreign-market reviewer.
        if (reviewerMarkets is not null && !reviewerMarkets.Contains(verification.MarketCode))
        {
            return DecideApproveResult.Fail(
                VerificationReasonCode.InvalidStateForAction,
                "Verification not found.");
        }

        // State-machine guard — submitted/in-review → approved is allowed for the
        // reviewer; everything else is rejected up-front.
        if (!VerificationStateMachine.CanTransition(
                verification.State,
                VerificationState.Approved,
                VerificationActorKind.Reviewer))
        {
            return DecideApproveResult.Fail(
                VerificationReasonCode.InvalidStateForAction,
                $"Verification is in state '{verification.State.ToWireValue()}'; cannot approve.");
        }

        // Resolve schema for ExpiresAt computation.
        var schema = await db.MarketSchemas
            .SingleOrDefaultAsync(
                s => s.MarketCode == verification.MarketCode && s.Version == verification.SchemaVersion,
                ct);
        if (schema is null)
        {
            return DecideApproveResult.Fail(
                VerificationReasonCode.MarketUnsupported,
                "Schema referenced by the verification could not be loaded.");
        }

        // Document scan-status invariant per AttachDocumentRequest contract:
        // every non-purged document must be `clean` before the reviewer can
        // approve. We reject on anything-but-`clean` (pending OR a row that
        // somehow flipped to infected/error post-attach) so the approval
        // can't land against a file the async scanner later flags. CR R1
        // Critical: previously this only rejected `pending`, leaving a
        // window where an infected row that was attached as clean and
        // later updated could still be approved.
        var nonCleanScan = await db.Documents
            .AsNoTracking()
            .Where(d => d.VerificationId == verification.Id
                     && d.PurgedAt == null
                     && d.ScanStatus != "clean")
            .Select(d => d.ScanStatus)
            .FirstOrDefaultAsync(ct);
        if (nonCleanScan is not null)
        {
            return DecideApproveResult.Fail(
                nonCleanScan == "pending"
                    ? VerificationReasonCode.DocumentScanPending
                    : VerificationReasonCode.DocumentScanInfected,
                $"Cannot approve while one or more attached documents have scan_status='{nonCleanScan}'.");
        }

        var priorState = verification.State;

        // Supersession path — validate chain invariants BEFORE mutating any rows.
        // CodeRabbit R2-5: defer state mutations until checks pass; otherwise a
        // failed-invariant return leaves dirty change-tracker state in this scope
        // that another caller might accidentally persist.
        Entities.Verification? superseded = null;
        if (verification.SupersedesId is { } supersedesId)
        {
            superseded = await db.Verifications
                .SingleOrDefaultAsync(v => v.Id == supersedesId, ct);

            if (superseded is null
                || superseded.CustomerId != verification.CustomerId
                || superseded.MarketCode != verification.MarketCode
                || superseded.State != VerificationState.Approved)
            {
                logger.LogWarning(
                    "Verification {VerificationId} approval blocked: supersedes_id={SupersedesId} chain invariants failed (exists={Exists}, customer_match={CustomerMatch}, market_match={MarketMatch}, state={State}).",
                    verification.Id,
                    supersedesId,
                    superseded is not null,
                    superseded?.CustomerId == verification.CustomerId,
                    superseded?.MarketCode == verification.MarketCode,
                    superseded?.State.ToWireValue() ?? "null");

                return DecideApproveResult.Fail(
                    VerificationReasonCode.InvalidStateForAction,
                    "supersession chain is invalid — the prior approval is missing, owned by another customer, in a different market, or no longer in approved state.");
            }
        }

        // All invariants passed — now mutate state and stage transitions atomically.
        verification.State = VerificationState.Approved;
        verification.DecidedAt = nowUtc;
        verification.DecidedBy = reviewerId;
        verification.ExpiresAt = nowUtc.AddDays(schema.ExpiryDays);
        verification.UpdatedAt = nowUtc;

        db.StateTransitions.Add(new VerificationStateTransition
        {
            Id = Guid.NewGuid(),
            VerificationId = verification.Id,
            MarketCode = verification.MarketCode,
            PriorState = priorState.ToWireValue(),
            NewState = VerificationState.Approved.ToWireValue(),
            ActorKind = VerificationActorKind.Reviewer.ToWireValue(),
            ActorId = reviewerId,
            Reason = ComposeReasonForLedger(request.Reason),
            MetadataJson = SerializeReasonMetadata(request.Reason),
            OccurredAt = nowUtc,
        });

        if (superseded is not null)
        {
            superseded.State = VerificationState.Superseded;
            superseded.SupersededById = verification.Id;
            superseded.UpdatedAt = nowUtc;

            // T096 retention wiring: stamp purge_after on the superseded
            // verification's documents using its OWN snapshotted schema (not
            // the renewal's), since retention semantics belong to the row
            // entering the terminal state. Same MarketCode + SchemaVersion
            // tuple — we already have `schema` loaded for that lookup.
            var supersededRetentionMonths = schema.RetentionMonths;
            var supersededPurgeAfter = nowUtc.AddMonths(supersededRetentionMonths);
            var supersededDocs = await db.Documents
                .Where(d => d.VerificationId == superseded.Id && d.PurgedAt == null && d.PurgeAfter == null)
                .ToListAsync(ct);
            foreach (var doc in supersededDocs)
            {
                doc.PurgeAfter = supersededPurgeAfter;
            }

            db.StateTransitions.Add(new VerificationStateTransition
            {
                Id = Guid.NewGuid(),
                VerificationId = superseded.Id,
                MarketCode = superseded.MarketCode,
                PriorState = VerificationState.Approved.ToWireValue(),
                NewState = VerificationState.Superseded.ToWireValue(),
                ActorKind = VerificationActorKind.System.ToWireValue(),
                ActorId = null,
                Reason = "renewal_approved",
                MetadataJson = JsonSerializer.Serialize(new { renewal_id = verification.Id }),
                OccurredAt = nowUtc,
            });
        }

        // Rebuild eligibility cache — the customer's eligibility just changed in this market.
        await eligibilityInvalidator.RebuildAsync(verification.CustomerId, verification.MarketCode, db, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // xmin mismatch — another reviewer raced this approval.
            return DecideApproveResult.Fail(
                VerificationReasonCode.AlreadyDecided,
                "Verification was decided by another reviewer.");
        }

        // Audit — best-effort; failure does NOT roll back the approval (FR-034).
        try
        {
            await auditPublisher.PublishAsync(new AuditEvent(
                    ActorId: reviewerId,
                    ActorRole: "reviewer",
                    Action: "verification.state_changed",
                    EntityType: "verification",
                    EntityId: verification.Id,
                    BeforeState: new { state = priorState.ToWireValue() },
                    AfterState: new
                    {
                        state = VerificationState.Approved.ToWireValue(),
                        expires_at = verification.ExpiresAt,
                        decided_at = verification.DecidedAt,
                        decided_by = reviewerId,
                        market_code = verification.MarketCode,
                        schema_version = verification.SchemaVersion,
                        supersedes_id = verification.SupersedesId,
                        reason_en = request.Reason.En,
                        reason_ar = request.Reason.Ar,
                    },
                    Reason: "reviewer_approve"),
                ct);

            if (superseded is not null && superseded.SupersededById == verification.Id)
            {
                await auditPublisher.PublishAsync(new AuditEvent(
                        ActorId: reviewerId,
                        ActorRole: "system",
                        Action: "verification.state_changed",
                        EntityType: "verification",
                        EntityId: superseded.Id,
                        BeforeState: new { state = VerificationState.Approved.ToWireValue() },
                        AfterState: new
                        {
                            state = VerificationState.Superseded.ToWireValue(),
                            superseded_by_id = verification.Id,
                        },
                        Reason: "renewal_approved"),
                    ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Verification {VerificationId} approved but audit publish failed; subscriber catch-up will reconcile.",
                verification.Id);
        }

        // In-process domain events (spec contracts §5). Best-effort publish per
        // FR-034; subscriber failure does NOT roll back the approval. Spec 025
        // (Notifications) consumes these to drive customer-facing emails/push.
        try
        {
            await domainPublisher.PublishAsync(new VerificationDomainEvents.VerificationApproved(
                VerificationId: verification.Id,
                CustomerId: verification.CustomerId,
                MarketCode: verification.MarketCode,
                LocaleHint: "en"), ct);
            if (superseded is not null)
            {
                await domainPublisher.PublishAsync(new VerificationDomainEvents.VerificationSuperseded(
                    PriorVerificationId: superseded.Id,
                    NewVerificationId: verification.Id,
                    CustomerId: verification.CustomerId,
                    MarketCode: superseded.MarketCode), ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Verification {VerificationId} approved but domain publish failed; subscriber catch-up will reconcile.",
                verification.Id);
        }

        return DecideApproveResult.Ok(new DecideApproveResponse(
            Id: verification.Id,
            State: VerificationState.Approved.ToWireValue(),
            DecidedAt: nowUtc,
            DecidedBy: reviewerId,
            ExpiresAt: verification.ExpiresAt!.Value,
            SupersededId: superseded?.Id));
    }

    private static string ComposeReasonForLedger(ReviewerReason reason)
    {
        // The ledger Reason column stores a human-readable summary; the full
        // bilingual payload is preserved in the metadata jsonb (and in the audit
        // event_data). Pick the first non-blank locale for ledger display.
        if (!string.IsNullOrWhiteSpace(reason.En)) return reason.En!;
        if (!string.IsNullOrWhiteSpace(reason.Ar)) return reason.Ar!;
        return "reviewer_approve";
    }

    private static string SerializeReasonMetadata(ReviewerReason reason)
    {
        var payload = new Dictionary<string, object?>(2);
        if (reason.En is not null) payload["reason_en"] = reason.En;
        if (reason.Ar is not null) payload["reason_ar"] = reason.Ar;
        return JsonSerializer.Serialize(payload);
    }
}

public sealed record DecideApproveResult(
    bool IsSuccess,
    DecideApproveResponse? Response,
    VerificationReasonCode? ReasonCode,
    string? Detail)
{
    public static DecideApproveResult Ok(DecideApproveResponse r) => new(true, r, null, null);
    public static DecideApproveResult Fail(VerificationReasonCode code, string detail) =>
        new(false, null, code, detail);
}
