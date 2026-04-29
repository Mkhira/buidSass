using System.Text.Json;
using BackendApi.Modules.AuditLog;
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
    TimeProvider clock,
    ILogger<DecideApproveHandler> logger)
{
    public async Task<DecideApproveResult> HandleAsync(
        Guid verificationId,
        Guid reviewerId,
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

        var priorState = verification.State;
        verification.State = VerificationState.Approved;
        verification.DecidedAt = nowUtc;
        verification.DecidedBy = reviewerId;
        verification.ExpiresAt = nowUtc.AddDays(schema.ExpiryDays);
        verification.UpdatedAt = nowUtc;

        // Append the state-transition ledger row in the same Tx.
        var transition = new VerificationStateTransition
        {
            Id = Guid.NewGuid(),
            VerificationId = verification.Id,
            PriorState = priorState.ToWireValue(),
            NewState = VerificationState.Approved.ToWireValue(),
            ActorKind = VerificationActorKind.Reviewer.ToWireValue(),
            ActorId = reviewerId,
            Reason = ComposeReasonForLedger(request.Reason),
            MetadataJson = SerializeReasonMetadata(request.Reason),
            OccurredAt = nowUtc,
        };
        db.StateTransitions.Add(transition);

        // Supersession path — if this verification has a SupersedesId, transition
        // the prior approval to superseded atomically.
        Entities.Verification? superseded = null;
        if (verification.SupersedesId is { } supersedesId)
        {
            superseded = await db.Verifications
                .SingleOrDefaultAsync(v => v.Id == supersedesId, ct);

            if (superseded is not null && superseded.State == VerificationState.Approved)
            {
                superseded.State = VerificationState.Superseded;
                superseded.SupersededById = verification.Id;
                superseded.UpdatedAt = nowUtc;

                db.StateTransitions.Add(new VerificationStateTransition
                {
                    Id = Guid.NewGuid(),
                    VerificationId = superseded.Id,
                    PriorState = VerificationState.Approved.ToWireValue(),
                    NewState = VerificationState.Superseded.ToWireValue(),
                    ActorKind = VerificationActorKind.System.ToWireValue(),
                    ActorId = null,
                    Reason = "renewal_approved",
                    MetadataJson = $"{{\"renewal_id\":\"{verification.Id}\"}}",
                    OccurredAt = nowUtc,
                });
            }
        }

        // Rebuild eligibility cache — the customer's eligibility just changed.
        await eligibilityInvalidator.RebuildAsync(verification.CustomerId, db, ct);

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
