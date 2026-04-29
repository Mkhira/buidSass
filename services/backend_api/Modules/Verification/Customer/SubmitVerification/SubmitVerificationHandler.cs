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

        // 1.5. US5 / T101 dynamic schema validation. The endpoint already
        // enforced shape (non-null body, length caps); this pass walks the
        // active schema's required_fields jsonb so a market schema bump (new
        // field, tighter regex, different enum) takes effect without code
        // changes (FR-026 / FR-029 — schema-driven submission).
        var (schemaOk, schemaReason, schemaDetail) =
            SubmitVerificationValidator.ValidateAgainstSchema(request, schema.RequiredFieldsJson);
        if (!schemaOk)
        {
            return SubmitResult.Fail(schemaReason!.Value, schemaDetail ?? "Submission failed schema validation.");
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
        // Both branches are scoped to the current market — markets are
        // independently regulated (ADR-010), so a non-terminal KSA verification
        // must not block an unrelated EG submission/renewal.
        var marketScope = schema.MarketCode;
        if (priorApproval is null)
        {
            var hasOpen = await db.Verifications.AnyAsync(
                v => v.CustomerId == customerId
                  && v.MarketCode == marketScope
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
                    "Customer already has a non-terminal verification in flight for this market.");
            }

            // US6 / T107 — cooldown check with FR-009 exception for revoked.
            // The most-recent terminal verification in this market drives the
            // policy: if it's rejected and inside the cooldown window, block;
            // if the customer was instead revoked (admin action) the cooldown
            // does NOT apply — the customer may submit fresh immediately.
            var cooldownDecision = await EvaluateCooldownAsync(customerId, marketScope, schema.CooldownDays, nowUtc, ct);
            if (cooldownDecision is { } block)
            {
                return block;
            }
        }
        else
        {
            // Renewal mode: the prior approval is excluded from the in-flight set
            // (it's in Approved which is non-terminal). Anything else non-terminal
            // in the same market is a duplicate pending renewal — reject.
            var priorApprovalId = priorApproval.Id;
            var hasOpenRenewal = await db.Verifications.AnyAsync(
                v => v.CustomerId == customerId
                  && v.MarketCode == marketScope
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
                    "A non-terminal renewal already exists for this customer in this market.");
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
            // Phase 5 / US3: capture the restriction-policy view at submission
            // time for FR-026 audit replay. IProductRestrictionPolicy itself is
            // per-SKU (declared in Modules/Shared/, owned by spec 005), so the
            // snapshot here records the *context* a future replay needs to
            // re-derive the decision: the contract version, the market, the
            // profession, and the timestamp. When spec 005 ships its production
            // binding, the snapshot can be enriched with the policy registry's
            // identity/version. Voided rows keep this snapshot per FR-027 so
            // the audit history retains the customer's submission context even
            // when they cannot resubmit against the row.
            RestrictionPolicySnapshotJson = SerializeRestrictionPolicySnapshot(
                schema.MarketCode,
                request.Profession,
                nowUtc),
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

    /// <summary>
    /// US6 / T107 — evaluate FR-009 cooldown. Looks up the customer's
    /// most-recent terminal verification in this market and:
    /// <list type="bullet">
    ///   <item>If <c>rejected</c> AND inside the cooldown window → block.</item>
    ///   <item>If <c>revoked</c> → no cooldown (FR-009).</item>
    ///   <item>Otherwise (expired / superseded / void) → no cooldown.</item>
    /// </list>
    /// The cooldown window is computed from the row's own DecidedAt + the
    /// snapshotted CooldownDays (we use the CURRENT schema's CooldownDays as
    /// the default — the rejected row's snapshot is canonical but a small
    /// drift between snapshots is acceptable for a customer-facing window).
    /// </summary>
    private async Task<SubmitResult?> EvaluateCooldownAsync(
        Guid customerId,
        string marketCode,
        int cooldownDays,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        if (cooldownDays <= 0)
        {
            return null;
        }

        var mostRecentTerminal = await db.Verifications
            .AsNoTracking()
            .Where(v => v.CustomerId == customerId
                     && v.MarketCode == marketCode
                     && (v.State == VerificationState.Rejected
                         || v.State == VerificationState.Revoked))
            .OrderByDescending(v => v.DecidedAt ?? v.UpdatedAt)
            .Select(v => new { v.State, DecidedAt = v.DecidedAt ?? v.UpdatedAt })
            .FirstOrDefaultAsync(ct);

        if (mostRecentTerminal is null)
        {
            return null;
        }

        // FR-009 — revoked customers may resubmit immediately.
        if (mostRecentTerminal.State == VerificationState.Revoked)
        {
            return null;
        }

        // Rejected — block until DecidedAt + cooldownDays.
        var cooldownUntil = mostRecentTerminal.DecidedAt.AddDays(cooldownDays);
        if (nowUtc < cooldownUntil)
        {
            return SubmitResult.Fail(
                VerificationReasonCode.CooldownActive,
                $"Customer is in cooldown after a rejection. Cooldown ends at {cooldownUntil:u}.");
        }

        return null;
    }

    /// <summary>
    /// Captures the per-(market, profession, contract-version) view at
    /// submission time so FR-026 audit-replay can reconstruct the eligibility
    /// context this row was decided under. The shape is forward-compatible:
    /// when spec 005 ships its policy registry, the snapshot can carry the
    /// registry's identity/version too without breaking older rows.
    /// </summary>
    private static string SerializeRestrictionPolicySnapshot(
        string marketCode,
        string profession,
        DateTimeOffset capturedAt) =>
        JsonSerializer.Serialize(new
        {
            version = "v1",
            market_code = marketCode,
            profession,
            captured_at = capturedAt,
            policy_source = "verification_submission_v1",
        });

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
