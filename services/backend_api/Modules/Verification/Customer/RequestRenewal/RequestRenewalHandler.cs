using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Verification.Customer.RequestRenewal;

/// <summary>
/// Spec 020 contracts §2.7 / tasks T059. Opens a renewal verification only
/// inside the earliest reminder window of the customer's active approval.
/// Validates:
/// <list type="bullet">
///   <item><c>no_active_approval</c> — customer has no <c>approved</c> row;</item>
///   <item><c>renewal_window_not_open</c> — outside the earliest reminder
///         window (now &lt; expires_at - earliest_reminder_window);</item>
///   <item><c>renewal_already_pending</c> — a non-terminal renewal already
///         points at this approval.</item>
/// </list>
/// On success, INSERTs a new <c>verifications</c> row with
/// <c>state=submitted</c>, <c>supersedes_id</c> = approval.id, schema_version
/// = the CURRENT active schema for the market (renewal customers verify
/// against the latest required-fields list, not the prior approval's snapshot).
/// The prior approval STAYS approved until the renewal is itself approved
/// (FR-010).
/// </summary>
public sealed class RequestRenewalHandler(
    VerificationDbContext db,
    IAuditEventPublisher auditPublisher,
    TimeProvider clock,
    ILogger<RequestRenewalHandler> logger)
{
    public Task<RenewalResult> HandleAsync(
        Guid customerId,
        RequestRenewalRequest request,
        CancellationToken ct)
        => HandleAsync(customerId, marketCode: "ksa", request, ct);

    public async Task<RenewalResult> HandleAsync(
        Guid customerId,
        string marketCode,
        RequestRenewalRequest request,
        CancellationToken ct)
    {
        var nowUtc = clock.GetUtcNow();

        // 1. Find customer's active approval IN THIS MARKET — most recent, not
        //    superseded. Customers can hold independent EG/KSA approvals; the
        //    handler MUST renew the right one (ADR-010).
        var approval = await db.Verifications
            .Where(v => v.CustomerId == customerId
                     && v.MarketCode == marketCode
                     && v.State == VerificationState.Approved)
            .OrderByDescending(v => v.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        if (approval is null)
        {
            return RenewalResult.Fail(
                VerificationReasonCode.RenewalNotEligible,
                "no_active_approval — customer has no approved verification to renew in this market.");
        }

        // 2. Inside earliest reminder window?
        if (approval.ExpiresAt is null)
        {
            return RenewalResult.Fail(
                VerificationReasonCode.RenewalNotEligible,
                "Active approval has no expires_at; renewal not applicable.");
        }

        // Use the approval's snapshotted schema for the reminder window — that's
        // what the customer was promised at approval time.
        var approvalSchema = await db.MarketSchemas
            .AsNoTracking()
            .Where(s => s.MarketCode == approval.MarketCode && s.Version == approval.SchemaVersion)
            .SingleOrDefaultAsync(ct);

        var earliestReminderDays = ParseEarliestReminderWindowDays(
            approvalSchema?.ReminderWindowsDaysJson);

        var renewalOpensAt = approval.ExpiresAt.Value.AddDays(-earliestReminderDays);
        if (nowUtc < renewalOpensAt)
        {
            return RenewalResult.Fail(
                VerificationReasonCode.RenewalNotEligible,
                $"renewal_window_not_open — renewal opens at {renewalOpensAt:u}.");
        }

        // 3. No other non-terminal renewal pointing at this approval.
        var pendingRenewal = await db.Verifications
            .AnyAsync(v => v.SupersedesId == approval.Id
                        && (v.State == VerificationState.Submitted
                         || v.State == VerificationState.InReview
                         || v.State == VerificationState.InfoRequested),
                ct);
        if (pendingRenewal)
        {
            return RenewalResult.Fail(
                VerificationReasonCode.RenewalAlreadyPending,
                "renewal_already_pending — a renewal of this approval is already under review.");
        }

        // 4. Resolve the schema for the renewal — CURRENT active schema, not the
        //    snapshotted one. Renewal customers verify against the latest rules.
        var currentSchema = await db.MarketSchemas
            .AsNoTracking()
            .Where(s => s.MarketCode == approval.MarketCode && s.EffectiveTo == null)
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync(ct);
        if (currentSchema is null)
        {
            return RenewalResult.Fail(
                VerificationReasonCode.MarketUnsupported,
                "No active schema found for the approval's market.");
        }

        // 5. Carry profession + regulator_identifier forward, allowing override.
        var profession = string.IsNullOrWhiteSpace(request.Profession)
            ? approval.Profession
            : request.Profession;
        var regulatorIdentifier = string.IsNullOrWhiteSpace(request.RegulatorIdentifier)
            ? approval.RegulatorIdentifier
            : request.RegulatorIdentifier;

        // 6. Insert the renewal row + initial state-transition.
        var renewalId = Guid.NewGuid();
        db.Verifications.Add(new Entities.Verification
        {
            Id = renewalId,
            CustomerId = customerId,
            MarketCode = approval.MarketCode,
            SchemaVersion = currentSchema.Version,
            Profession = profession,
            RegulatorIdentifier = regulatorIdentifier,
            State = VerificationState.Submitted,
            SubmittedAt = nowUtc,
            SupersedesId = approval.Id,
            // Carry the prior approval's restriction-policy snapshot forward so
            // the renewal is reviewed against the same restriction context the
            // approval was granted under (Principle 8 — reusable restriction +
            // eligibility model). The reviewer's approval handler refreshes the
            // snapshot via IProductRestrictionPolicy at decision time per the
            // submission flow; until then renewal audit replay keeps a non-empty
            // policy view.
            RestrictionPolicySnapshotJson = string.IsNullOrWhiteSpace(approval.RestrictionPolicySnapshotJson)
                ? "{}"
                : approval.RestrictionPolicySnapshotJson,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        });

        db.StateTransitions.Add(new VerificationStateTransition
        {
            Id = Guid.NewGuid(),
            VerificationId = renewalId,
            MarketCode = approval.MarketCode,
            PriorState = VerificationStateMachine.PriorStateNoneWire,
            NewState = VerificationState.Submitted.ToWireValue(),
            ActorKind = VerificationActorKind.Customer.ToWireValue(),
            ActorId = customerId,
            Reason = "customer_renewal_request",
            MetadataJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["supersedes_id"] = approval.Id,
                ["prior_expires_at"] = approval.ExpiresAt,
                ["renewal_window_opens_at"] = renewalOpensAt,
            }),
            OccurredAt = nowUtc,
        });

        await db.SaveChangesAsync(ct);

        try
        {
            await auditPublisher.PublishAsync(new AuditEvent(
                    ActorId: customerId,
                    ActorRole: "customer",
                    Action: "verification.state_changed",
                    EntityType: "verification",
                    EntityId: renewalId,
                    BeforeState: null,
                    AfterState: new
                    {
                        state = VerificationState.Submitted.ToWireValue(),
                        market_code = approval.MarketCode,
                        schema_version = currentSchema.Version,
                        supersedes_id = approval.Id,
                    },
                    Reason: "customer_renewal_request"),
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Renewal {VerificationId} created but audit publish failed.",
                renewalId);
        }

        return RenewalResult.Ok(new RequestRenewalResponse(
            Id: renewalId,
            SupersedesId: approval.Id,
            State: VerificationState.Submitted.ToWireValue(),
            SubmittedAt: nowUtc));
    }

    private static int ParseEarliestReminderWindowDays(string? reminderWindowsJson)
    {
        if (string.IsNullOrWhiteSpace(reminderWindowsJson))
        {
            return 30;
        }
        try
        {
            var arr = JsonSerializer.Deserialize<int[]>(reminderWindowsJson);
            if (arr is null || arr.Length == 0)
            {
                return 30;
            }
            var max = 0;
            foreach (var d in arr)
            {
                if (d > max) max = d;
            }
            return max == 0 ? 30 : max;
        }
        catch (JsonException)
        {
            return 30;
        }
    }
}

public sealed record RenewalResult(
    bool IsSuccess,
    RequestRenewalResponse? Response,
    VerificationReasonCode? ReasonCode,
    string? Detail)
{
    public static RenewalResult Ok(RequestRenewalResponse r) => new(true, r, null, null);
    public static RenewalResult Fail(VerificationReasonCode code, string detail) =>
        new(false, null, code, detail);
}
