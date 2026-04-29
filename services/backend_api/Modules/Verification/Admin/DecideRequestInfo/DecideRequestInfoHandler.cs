using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Verification.Admin.Common;
using BackendApi.Modules.Verification.Eligibility;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Verification.Admin.DecideRequestInfo;

/// <summary>
/// Spec 020 contracts §3.5 / tasks T074. Reviewer asks the customer for more
/// information. Two semantic differences from approve / reject:
/// <list type="bullet">
///   <item>State target is <see cref="VerificationState.InfoRequested"/>
///         (non-terminal — customer's resubmission flips it back to in_review).</item>
///   <item>SLA timer pauses while in this state (FR-039). The transition row's
///         metadata jsonb captures <c>paused_at = decided_at</c> so the queue
///         handler + audit replay can compute pause-aware ages.</item>
///   <item>The verification's <c>decided_at</c> is intentionally NOT set —
///         info_requested is non-terminal; only the eventual approve/reject
///         stamps decided_at. The transition row's <c>occurred_at</c> is the
///         authoritative pause timestamp.</item>
/// </list>
/// Cache rebuild runs anyway per T085 — the invariant is "every transition
/// rebuilds authority in the same Tx" rather than "rebuild only when the
/// outcome would differ". A no-op rebuild is cheap and keeps the contract
/// uniform across handlers.
/// </summary>
public sealed class DecideRequestInfoHandler(
    VerificationDbContext db,
    EligibilityCacheInvalidator eligibilityInvalidator,
    IAuditEventPublisher auditPublisher,
    TimeProvider clock,
    ILogger<DecideRequestInfoHandler> logger)
{
    public async Task<DecideRequestInfoResult> HandleAsync(
        Guid verificationId,
        Guid reviewerId,
        DecideRequestInfoRequest request,
        CancellationToken ct)
    {
        var nowUtc = clock.GetUtcNow();

        var verification = await db.Verifications
            .SingleOrDefaultAsync(v => v.Id == verificationId, ct);

        if (verification is null)
        {
            return DecideRequestInfoResult.Fail(
                VerificationReasonCode.InvalidStateForAction,
                "Verification not found.");
        }

        if (!VerificationStateMachine.CanTransition(
                verification.State,
                VerificationState.InfoRequested,
                VerificationActorKind.Reviewer))
        {
            return DecideRequestInfoResult.Fail(
                VerificationReasonCode.InvalidStateForAction,
                $"Verification is in state '{verification.State.ToWireValue()}'; cannot request-info.");
        }

        var priorState = verification.State;
        verification.State = VerificationState.InfoRequested;
        verification.UpdatedAt = nowUtc;

        // SLA-timer pause begins now. The pause stamp is on the transition row's
        // metadata so the queue handler can find it without an extra query at
        // the verification-row level.
        db.StateTransitions.Add(new VerificationStateTransition
        {
            Id = Guid.NewGuid(),
            VerificationId = verification.Id,
            MarketCode = verification.MarketCode,
            PriorState = priorState.ToWireValue(),
            NewState = VerificationState.InfoRequested.ToWireValue(),
            ActorKind = VerificationActorKind.Reviewer.ToWireValue(),
            ActorId = reviewerId,
            Reason = ReviewerReasonValidator.ComposeLedgerSummary(request.Reason, "reviewer_request_info"),
            MetadataJson = ReviewerReasonValidator.SerializeMetadata(
                request.Reason,
                new Dictionary<string, object?>
                {
                    ["paused_at"] = nowUtc,
                    ["sla_pause_kind"] = "info_requested",
                }),
            OccurredAt = nowUtc,
        });

        // Rebuild eligibility cache inside the same Tx (T085). info_requested
        // doesn't change ineligibility class, but the rebuild keeps every
        // transition path uniform.
        await eligibilityInvalidator.RebuildAsync(verification.CustomerId, verification.MarketCode, db, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DecideRequestInfoResult.Fail(
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
                        state = VerificationState.InfoRequested.ToWireValue(),
                        paused_at = nowUtc,
                        market_code = verification.MarketCode,
                        schema_version = verification.SchemaVersion,
                        reason_en = request.Reason.En,
                        reason_ar = request.Reason.Ar,
                    },
                    Reason: "reviewer_request_info"),
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Verification {VerificationId} request-info'd but audit publish failed.",
                verification.Id);
        }

        return DecideRequestInfoResult.Ok(new DecideRequestInfoResponse(
            Id: verification.Id,
            State: VerificationState.InfoRequested.ToWireValue(),
            RequestedAt: nowUtc,
            RequestedBy: reviewerId));
    }
}

public sealed record DecideRequestInfoResult(
    bool IsSuccess,
    DecideRequestInfoResponse? Response,
    VerificationReasonCode? ReasonCode,
    string? Detail)
{
    public static DecideRequestInfoResult Ok(DecideRequestInfoResponse r) => new(true, r, null, null);
    public static DecideRequestInfoResult Fail(VerificationReasonCode code, string detail) =>
        new(false, null, code, detail);
}
