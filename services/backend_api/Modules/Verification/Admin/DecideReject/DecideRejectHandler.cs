using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Verification.Admin.Common;
using BackendApi.Modules.Verification.Eligibility;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Verification.Admin.DecideReject;

/// <summary>
/// Spec 020 contracts §3.4 / tasks T073. Reviewer rejection. Mirrors
/// <see cref="DecideApprove.DecideApproveHandler"/> with two semantic
/// differences:
/// <list type="bullet">
///   <item>State target is <see cref="VerificationState.Rejected"/> (terminal).</item>
///   <item><c>cooldown_until = decided_at + market.cooldown_days</c> is computed
///         and returned to the customer-facing UI (FR-009).</item>
///   <item>No supersession path — rejection never affects a prior approval.</item>
/// </list>
/// </summary>
public sealed class DecideRejectHandler(
    VerificationDbContext db,
    EligibilityCacheInvalidator eligibilityInvalidator,
    IAuditEventPublisher auditPublisher,
    TimeProvider clock,
    ILogger<DecideRejectHandler> logger)
{
    public async Task<DecideRejectResult> HandleAsync(
        Guid verificationId,
        Guid reviewerId,
        DecideRejectRequest request,
        CancellationToken ct)
    {
        var nowUtc = clock.GetUtcNow();

        var verification = await db.Verifications
            .SingleOrDefaultAsync(v => v.Id == verificationId, ct);

        if (verification is null)
        {
            return DecideRejectResult.Fail(
                VerificationReasonCode.InvalidStateForAction,
                "Verification not found.");
        }

        if (!VerificationStateMachine.CanTransition(
                verification.State,
                VerificationState.Rejected,
                VerificationActorKind.Reviewer))
        {
            return DecideRejectResult.Fail(
                VerificationReasonCode.InvalidStateForAction,
                $"Verification is in state '{verification.State.ToWireValue()}'; cannot reject.");
        }

        var schema = await db.MarketSchemas
            .SingleOrDefaultAsync(
                s => s.MarketCode == verification.MarketCode && s.Version == verification.SchemaVersion,
                ct);
        if (schema is null)
        {
            return DecideRejectResult.Fail(
                VerificationReasonCode.MarketUnsupported,
                "Schema referenced by the verification could not be loaded.");
        }

        var priorState = verification.State;
        verification.State = VerificationState.Rejected;
        verification.DecidedAt = nowUtc;
        verification.DecidedBy = reviewerId;
        verification.UpdatedAt = nowUtc;

        var cooldownUntil = nowUtc.AddDays(schema.CooldownDays);

        db.StateTransitions.Add(new VerificationStateTransition
        {
            Id = Guid.NewGuid(),
            VerificationId = verification.Id,
            PriorState = priorState.ToWireValue(),
            NewState = VerificationState.Rejected.ToWireValue(),
            ActorKind = VerificationActorKind.Reviewer.ToWireValue(),
            ActorId = reviewerId,
            Reason = ReviewerReasonValidator.ComposeLedgerSummary(request.Reason, "reviewer_reject"),
            MetadataJson = ReviewerReasonValidator.SerializeMetadata(
                request.Reason,
                new Dictionary<string, object?> { ["cooldown_until"] = cooldownUntil }),
            OccurredAt = nowUtc,
        });

        // Eligibility cache rebuild — rejection means no active approval.
        await eligibilityInvalidator.RebuildAsync(verification.CustomerId, db, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DecideRejectResult.Fail(
                VerificationReasonCode.AlreadyDecided,
                "Verification was decided by another reviewer.");
        }

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
                        state = VerificationState.Rejected.ToWireValue(),
                        decided_at = verification.DecidedAt,
                        decided_by = reviewerId,
                        market_code = verification.MarketCode,
                        schema_version = verification.SchemaVersion,
                        cooldown_until = cooldownUntil,
                        reason_en = request.Reason.En,
                        reason_ar = request.Reason.Ar,
                    },
                    Reason: "reviewer_reject"),
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Verification {VerificationId} rejected but audit publish failed; subscriber catch-up will reconcile.",
                verification.Id);
        }

        return DecideRejectResult.Ok(new DecideRejectResponse(
            Id: verification.Id,
            State: VerificationState.Rejected.ToWireValue(),
            DecidedAt: nowUtc,
            DecidedBy: reviewerId,
            CooldownUntil: cooldownUntil));
    }
}

public sealed record DecideRejectResult(
    bool IsSuccess,
    DecideRejectResponse? Response,
    VerificationReasonCode? ReasonCode,
    string? Detail)
{
    public static DecideRejectResult Ok(DecideRejectResponse r) => new(true, r, null, null);
    public static DecideRejectResult Fail(VerificationReasonCode code, string detail) =>
        new(false, null, code, detail);
}
