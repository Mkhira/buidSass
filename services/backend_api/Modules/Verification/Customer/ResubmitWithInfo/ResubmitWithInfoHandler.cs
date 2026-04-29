using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Verification.Customer.ResubmitWithInfo;

/// <summary>
/// Spec 020 contracts §2.6 / tasks T058. Customer resubmits after info-request.
/// Validates:
/// <list type="bullet">
///   <item>customer owns the verification (404 NotFound for foreign id);</item>
///   <item>verification is currently in <c>info_requested</c> (any other
///         state rejects with InvalidStateForAction);</item>
///   <item>at least one document was attached after the most-recent
///         <c>info_requested</c> transition — otherwise rejects with
///         <c>no_changes_provided</c> (FR-016 — resubmit MUST carry change);</item>
///   <item>state transitions <c>info_requested → in_review</c> via the
///         state-machine guard (only Customer actor can drive this edge).</item>
/// </list>
/// Original <c>submitted_at</c> is preserved; <c>updated_at</c> bumps; an
/// audit event with reason "customer_resubmit_with_info" is published.
/// </summary>
public sealed class ResubmitWithInfoHandler(
    VerificationDbContext db,
    IAuditEventPublisher auditPublisher,
    TimeProvider clock,
    ILogger<ResubmitWithInfoHandler> logger)
{
    public async Task<ResubmitResult> HandleAsync(
        Guid customerId,
        Guid verificationId,
        ResubmitWithInfoRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Acknowledgement))
        {
            return ResubmitResult.Fail(
                VerificationReasonCode.RequiredFieldMissing,
                "acknowledgement is required.");
        }

        var nowUtc = clock.GetUtcNow();

        var verification = await db.Verifications
            .Where(v => v.Id == verificationId && v.CustomerId == customerId)
            .SingleOrDefaultAsync(ct);

        if (verification is null)
        {
            return ResubmitResult.NotFound;
        }

        if (verification.State != VerificationState.InfoRequested)
        {
            return ResubmitResult.Fail(
                VerificationReasonCode.InvalidStateForAction,
                $"Verification is in state '{verification.State.ToWireValue()}'; only info-requested can be resubmitted.");
        }

        // FR-016 — "at least one new document or modified field". The MVP
        // semantic: a new document MUST have been attached since the most-
        // recent info-request transition. This catches the empty-resubmit
        // case without requiring the customer to explicitly modify fields.
        var lastInfoRequestedAt = await db.StateTransitions
            .Where(t => t.VerificationId == verificationId
                     && t.NewState == VerificationState.InfoRequested.ToWireValue())
            .OrderByDescending(t => t.OccurredAt)
            .Select(t => (DateTimeOffset?)t.OccurredAt)
            .FirstOrDefaultAsync(ct);

        if (lastInfoRequestedAt is null)
        {
            // Should be unreachable given the state guard above, but be safe.
            return ResubmitResult.Fail(
                VerificationReasonCode.InvalidStateForAction,
                "No info_requested transition found for this verification.");
        }

        var newDocsAttached = await db.Documents
            .AnyAsync(d => d.VerificationId == verificationId
                        && d.UploadedAt > lastInfoRequestedAt.Value
                        && d.PurgedAt == null,
                ct);

        if (!newDocsAttached)
        {
            return ResubmitResult.Fail(
                VerificationReasonCode.RequiredFieldMissing,
                "no_changes_provided — attach at least one new document before resubmitting.");
        }

        // State machine: info_requested → in_review (customer actor only).
        VerificationStateMachine.EnsureCanTransitionOrThrow(
            verification.State,
            VerificationState.InReview,
            VerificationActorKind.Customer);

        var priorState = verification.State;
        verification.State = VerificationState.InReview;
        verification.UpdatedAt = nowUtc;

        db.StateTransitions.Add(new VerificationStateTransition
        {
            Id = Guid.NewGuid(),
            VerificationId = verification.Id,
            MarketCode = verification.MarketCode,
            PriorState = priorState.ToWireValue(),
            NewState = VerificationState.InReview.ToWireValue(),
            ActorKind = VerificationActorKind.Customer.ToWireValue(),
            ActorId = customerId,
            Reason = "customer_resubmit_with_info",
            MetadataJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["acknowledgement"] = request.Acknowledgement,
                ["resumed_from_paused_at"] = lastInfoRequestedAt,
            }),
            OccurredAt = nowUtc,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResubmitResult.Fail(
                VerificationReasonCode.OptimisticConcurrencyConflict,
                "Verification state changed concurrently. Reload and retry.");
        }

        try
        {
            await auditPublisher.PublishAsync(new AuditEvent(
                    ActorId: customerId,
                    ActorRole: "customer",
                    Action: "verification.state_changed",
                    EntityType: "verification",
                    EntityId: verification.Id,
                    BeforeState: new { state = priorState.ToWireValue() },
                    AfterState: new
                    {
                        state = VerificationState.InReview.ToWireValue(),
                        market_code = verification.MarketCode,
                        schema_version = verification.SchemaVersion,
                        original_submitted_at = verification.SubmittedAt,
                    },
                    Reason: "customer_resubmit_with_info"),
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Verification {VerificationId} resubmitted but audit publish failed.",
                verification.Id);
        }

        return ResubmitResult.Ok(new ResubmitWithInfoResponse(
            Id: verification.Id,
            State: VerificationState.InReview.ToWireValue(),
            SubmittedAt: verification.SubmittedAt,
            ResubmittedAt: nowUtc));
    }
}

public sealed record ResubmitResult(
    bool IsSuccess,
    bool IsNotFound,
    ResubmitWithInfoResponse? Response,
    VerificationReasonCode? ReasonCode,
    string? Detail)
{
    public static ResubmitResult Ok(ResubmitWithInfoResponse r) => new(true, false, r, null, null);
    public static ResubmitResult NotFound => new(false, true, null, null, null);
    public static ResubmitResult Fail(VerificationReasonCode code, string detail) =>
        new(false, false, null, code, detail);
}
