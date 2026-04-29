using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Admin.Common;
using BackendApi.Modules.Verification.Eligibility;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Verification.Admin.DecideRevoke;

/// <summary>
/// Spec 020 contracts §3.6. Reviewer with the <c>verification.revoke</c>
/// permission revokes an active approval. State machine: Approved → Revoked
/// (terminal). No cool-down per FR-009 (revocation is a regulatory action,
/// not a customer fault). Eligibility cache must be rebuilt — the customer
/// loses their eligibility immediately.
/// </summary>
public sealed class DecideRevokeHandler(
    VerificationDbContext db,
    EligibilityCacheInvalidator eligibilityInvalidator,
    IAuditEventPublisher auditPublisher,
    IVerificationDomainEventPublisher domainPublisher,
    TimeProvider clock,
    ILogger<DecideRevokeHandler> logger)
{
    public Task<DecideRevokeResult> HandleAsync(
        Guid verificationId,
        Guid reviewerId,
        DecideRevokeRequest request,
        CancellationToken ct)
        => HandleAsync(verificationId, reviewerId, reviewerMarkets: null, request, ct);

    public async Task<DecideRevokeResult> HandleAsync(
        Guid verificationId,
        Guid reviewerId,
        IReadOnlySet<string>? reviewerMarkets,
        DecideRevokeRequest request,
        CancellationToken ct)
    {
        var nowUtc = clock.GetUtcNow();

        var verification = await db.Verifications
            .SingleOrDefaultAsync(v => v.Id == verificationId, ct);

        if (verification is null)
        {
            return DecideRevokeResult.Fail(
                VerificationReasonCode.InvalidStateForAction,
                "Verification not found.");
        }

        if (reviewerMarkets is not null && !reviewerMarkets.Contains(verification.MarketCode))
        {
            return DecideRevokeResult.Fail(
                VerificationReasonCode.InvalidStateForAction,
                "Verification not found.");
        }

        if (!VerificationStateMachine.CanTransition(
                verification.State,
                VerificationState.Revoked,
                VerificationActorKind.Reviewer))
        {
            return DecideRevokeResult.Fail(
                VerificationReasonCode.InvalidStateForAction,
                $"Verification is in state '{verification.State.ToWireValue()}'; only approved verifications can be revoked.");
        }

        var priorState = verification.State;
        verification.State = VerificationState.Revoked;
        verification.UpdatedAt = nowUtc;
        // decided_at / decided_by were already set on the original approval; we
        // don't overwrite them — the audit trail captures the revocation moment
        // separately via the state-transition row.

        // T096 retention wiring: stamp purge_after on every non-purged document.
        var schema = await db.MarketSchemas
            .AsNoTracking()
            .Where(s => s.MarketCode == verification.MarketCode && s.Version == verification.SchemaVersion)
            .SingleOrDefaultAsync(ct);
        var retentionMonths = schema?.RetentionMonths ?? 24;
        var purgeAfter = nowUtc.AddMonths(retentionMonths);
        var documents = await db.Documents
            .Where(d => d.VerificationId == verification.Id && d.PurgedAt == null && d.PurgeAfter == null)
            .ToListAsync(ct);
        foreach (var doc in documents)
        {
            doc.PurgeAfter = purgeAfter;
        }

        db.StateTransitions.Add(new VerificationStateTransition
        {
            Id = Guid.NewGuid(),
            VerificationId = verification.Id,
            MarketCode = verification.MarketCode,
            PriorState = priorState.ToWireValue(),
            NewState = VerificationState.Revoked.ToWireValue(),
            ActorKind = VerificationActorKind.Reviewer.ToWireValue(),
            ActorId = reviewerId,
            Reason = ReviewerReasonValidator.ComposeLedgerSummary(request.Reason, "reviewer_revoke"),
            MetadataJson = ReviewerReasonValidator.SerializeMetadata(request.Reason),
            OccurredAt = nowUtc,
        });

        // Eligibility cache rebuild — customer goes from eligible → ineligible
        // (assuming no other active approval covers them).
        await eligibilityInvalidator.RebuildAsync(verification.CustomerId, verification.MarketCode, db, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DecideRevokeResult.Fail(
                VerificationReasonCode.AlreadyDecided,
                "Verification state changed by another actor.");
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
                        state = VerificationState.Revoked.ToWireValue(),
                        revoked_at = nowUtc,
                        revoked_by = reviewerId,
                        market_code = verification.MarketCode,
                        schema_version = verification.SchemaVersion,
                        reason_en = request.Reason.En,
                        reason_ar = request.Reason.Ar,
                    },
                    Reason: "reviewer_revoke"),
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Verification {VerificationId} revoked but audit publish failed.",
                verification.Id);
        }

        try
        {
            await domainPublisher.PublishAsync(new VerificationDomainEvents.VerificationRevoked(
                VerificationId: verification.Id,
                CustomerId: verification.CustomerId,
                MarketCode: verification.MarketCode,
                Reason: ReviewerReasonValidator.ComposeLedgerSummary(request.Reason, "reviewer_revoke"),
                LocaleHint: "en"), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Verification {VerificationId} revoked but domain publish failed.",
                verification.Id);
        }

        return DecideRevokeResult.Ok(new DecideRevokeResponse(
            Id: verification.Id,
            State: VerificationState.Revoked.ToWireValue(),
            RevokedAt: nowUtc,
            RevokedBy: reviewerId));
    }
}

public sealed record DecideRevokeResult(
    bool IsSuccess,
    DecideRevokeResponse? Response,
    VerificationReasonCode? ReasonCode,
    string? Detail)
{
    public static DecideRevokeResult Ok(DecideRevokeResponse r) => new(true, r, null, null);
    public static DecideRevokeResult Fail(VerificationReasonCode code, string detail) =>
        new(false, null, code, detail);
}
