using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.Pricing.Admin.Approvals;

/// <summary>
/// Spec 007-b commercial-approval endpoints (US5 / contract §8). The high-impact
/// gate (FR-025) routes drafts that exceed any per-market threshold here for a
/// second-pair-of-eyes co-sign before activation.
///
/// Concurrency (research §R12): self-approval is forbidden at the handler
/// layer (layer 1) AND at the DB layer via a unique <c>(target_kind,target_id)</c>
/// index on <c>pricing.commercial_approvals</c> (layer 2). The unique-violation
/// is mapped to <c>commercial.approval.already_recorded</c>.
/// </summary>
public static class CommercialApprovalEndpoints
{
    private const int CosignNoteMinLen = 10;

    public static IEndpointRouteBuilder MapCommercialApprovalEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/approvals");
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };

        group.MapGet("/pending", ListPendingAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Approver);
        group.MapPost("", RecordApprovalAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Approver);
        group.MapPost("/reject", RejectApprovalAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Approver);
        return builder;
    }

    // -------------------- GET /approvals/pending --------------------

    private static async Task<IResult> ListPendingAsync(
        HttpContext context,
        PricingDbContext db,
        CancellationToken ct)
    {
        var callerId = AdminCommercialResponseFactory.ResolveActorAccountId(context);

        // Scan draft coupons + promotions, evaluate the gate, exclude the
        // caller's own drafts. Pending = draft AND no approval row yet AND
        // gate trips for at least one of the rule's markets.
        var thresholds = await db.CommercialThresholds.AsNoTracking().ToListAsync(ct);

        var coupons = await db.Coupons.AsNoTracking()
            .Where(c => c.State == LifecycleState.Draft &&
                        c.AuthorActorId != callerId &&
                        c.DeletedAt == null)
            .ToListAsync(ct);
        var promotions = await db.Promotions.AsNoTracking()
            .Where(p => p.State == LifecycleState.Draft &&
                        p.AuthorActorId != callerId &&
                        p.DeletedAt == null)
            .ToListAsync(ct);
        var approvedIds = new HashSet<Guid>(
            await db.CommercialApprovals.AsNoTracking()
                .Select(a => a.TargetEntityId)
                .ToListAsync(ct));

        var items = new List<PendingApprovalRow>();

        foreach (var c in coupons)
        {
            if (approvedIds.Contains(c.Id)) continue;
            var policy = ResolveThresholdPolicy(thresholds, c.MarketCodes);
            if (policy is null) continue;
            var candidate = ToCouponCandidate(c);
            if (!HighImpactGate.IsTriggered(candidate, policy)) continue;
            items.Add(new PendingApprovalRow(
                "coupon", c.Id, c.AuthorActorId, c.CreatedAt, c.MarketCodes,
                c.LabelAr, c.LabelEn, SummariseTrigger(candidate, policy)));
        }
        foreach (var p in promotions)
        {
            if (approvedIds.Contains(p.Id)) continue;
            var policy = ResolveThresholdPolicy(thresholds, p.MarketCodes);
            if (policy is null) continue;
            var candidate = ToPromotionCandidate(p);
            if (!HighImpactGate.IsTriggered(candidate, policy)) continue;
            items.Add(new PendingApprovalRow(
                "promotion", p.Id, p.AuthorActorId, p.CreatedAt, p.MarketCodes,
                p.LabelAr, p.LabelEn, SummariseTrigger(candidate, policy)));
        }

        items = items.OrderByDescending(r => r.AuthoredAtUtc).ToList();
        return Results.Ok(new PendingApprovalListResponse(items));
    }

    // -------------------- POST /approvals --------------------

    private static async Task<IResult> RecordApprovalAsync(
        [FromBody] RecordApprovalRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        IPublisher events,
        TimeProvider time,
        CancellationToken ct)
    {
        var validation = ValidateApprovalRequest(request);
        if (validation is not null) return validation(context);

        var callerId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var nowUtc = time.GetUtcNow();
        var kind = request.TargetEntityKind.Trim().ToLowerInvariant();

        // Resolve author + verify the gate still trips. The route's
        // `commercial.approver` permission already excludes operators.
        Guid authorId;
        string[] marketCodes;
        bool gateStillTrips;
        if (kind == "coupon")
        {
            var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == request.TargetEntityId, ct);
            if (coupon is null || coupon.State != LifecycleState.Draft)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CommercialApprovalNotPending,
                    "Draft is not pending approval.");
            }
            authorId = coupon.AuthorActorId;
            marketCodes = coupon.MarketCodes;
            var policy = await ResolveThresholdAsync(db, marketCodes, ct);
            gateStillTrips = policy is not null && HighImpactGate.IsTriggered(ToCouponCandidate(coupon), policy);
        }
        else if (kind == "promotion")
        {
            var promo = await db.Promotions.FirstOrDefaultAsync(p => p.Id == request.TargetEntityId, ct);
            if (promo is null || promo.State != LifecycleState.Draft)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CommercialApprovalNotPending,
                    "Draft is not pending approval.");
            }
            authorId = promo.AuthorActorId;
            marketCodes = promo.MarketCodes;
            var policy = await ResolveThresholdAsync(db, marketCodes, ct);
            gateStillTrips = policy is not null && HighImpactGate.IsTriggered(ToPromotionCandidate(promo), policy);
        }
        else
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialApprovalNotPending,
                "Unknown target_entity_kind.");
        }

        // R12 layer 1: self-approval is forbidden.
        if (authorId == callerId)
        {
            return AdminCommercialResponseFactory.Problem(context, 403,
                CommercialReasonCode.CommercialSelfApprovalForbidden,
                "An approver cannot co-sign their own draft.");
        }

        if (!gateStillTrips)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialApprovalNotPending,
                "Draft no longer trips the high-impact gate; operator may activate directly.");
        }

        // Stage approval + advance the draft inside a single transaction so a
        // unique-violation on commercial_approvals rolls back the state flip.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var approvalRow = new CommercialApproval
        {
            Id = Guid.NewGuid(),
            TargetEntityKind = kind,
            TargetEntityId = request.TargetEntityId,
            AuthorActorId = authorId,
            ApproverActorId = callerId,
            CosignNote = request.CosignNote.Trim(),
            ApprovedAtUtc = nowUtc,
        };
        db.CommercialApprovals.Add(approvalRow);

        var publishApprovalAudit = audit.StageLocal(
            "commercial_approval", approvalRow.Id, "commercial.approval_recorded",
            callerId, CommercialPermissions.Approver,
            before: null,
            after: new { approvalRow.TargetEntityKind, approvalRow.TargetEntityId, approvalRow.AuthorActorId, approvalRow.ApproverActorId, outcome = "approved" },
            diff: null, reasonNote: approvalRow.CosignNote, correlationId: null, nowUtc);

        // Advance the draft using the same logic as POST /schedule but with
        // the gate explicitly bypassed (the approval row IS the bypass).
        bool activated;
        string newStateLabel;
        if (kind == "coupon")
        {
            var coupon = await db.Coupons.FirstAsync(c => c.Id == request.TargetEntityId, ct);
            if (coupon.ValidFrom is null || coupon.ValidTo is null)
            {
                await tx.RollbackAsync(ct);
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CouponScheduleInvalidWindow,
                    "valid_from and valid_to must be set before activation.");
            }
            if (!LifecycleStateMachine.TryTransition(
                    coupon.State, LifecycleTrigger.Schedule, nowUtc,
                    coupon.ValidFrom, coupon.ValidTo, out var newState, out var reason))
            {
                await tx.RollbackAsync(ct);
                return AdminCommercialResponseFactory.Problem(context, 400,
                    reason ?? CommercialReasonCode.CommercialRowInvalidTransition,
                    "Schedule transition rejected.");
            }
            var beforeC = new { state = coupon.State.ToString().ToLowerInvariant() };
            coupon.State = newState;
            coupon.StateChangedAtUtc = nowUtc;
            coupon.StateChangedByActorId = callerId;
            coupon.IsActive = newState == LifecycleState.Active;
            coupon.UpdatedAt = nowUtc;
            var afterC = new { state = coupon.State.ToString().ToLowerInvariant() };
            activated = newState == LifecycleState.Active;
            newStateLabel = afterC.state;

            var publishCouponAudit = audit.StageLocal(
                "coupon", coupon.Id, "coupon.lifecycle_transitioned",
                callerId, CommercialPermissions.Approver,
                beforeC, afterC,
                diff: new { state_change = new { from = beforeC.state, to = afterC.state }, via = "approval" },
                reasonNote: approvalRow.CosignNote, correlationId: null, nowUtc);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await tx.RollbackAsync(ct);
                return AdminCommercialResponseFactory.Problem(context, 409,
                    CommercialReasonCode.CommercialApprovalAlreadyRecorded,
                    "Another approver has already recorded an approval for this draft.");
            }
            await publishApprovalAudit(ct);
            await publishCouponAudit(ct);
            await tx.CommitAsync(ct);

            if (activated)
            {
                await events.Publish(new CouponActivated(
                    coupon.Id, coupon.Code, coupon.MarketCodes,
                    coupon.ValidFrom, coupon.ValidTo, nowUtc, callerId), ct);
            }
        }
        else
        {
            var promo = await db.Promotions.FirstAsync(p => p.Id == request.TargetEntityId, ct);
            if (promo.StartsAt is null || promo.EndsAt is null)
            {
                await tx.RollbackAsync(ct);
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.PromotionScheduleInvalidWindow,
                    "valid_from and valid_to must be set before activation.");
            }
            if (!LifecycleStateMachine.TryTransition(
                    promo.State, LifecycleTrigger.Schedule, nowUtc,
                    promo.StartsAt, promo.EndsAt, out var newState, out var reason))
            {
                await tx.RollbackAsync(ct);
                return AdminCommercialResponseFactory.Problem(context, 400,
                    reason ?? CommercialReasonCode.CommercialRowInvalidTransition,
                    "Schedule transition rejected.");
            }
            var beforeP = new { state = promo.State.ToString().ToLowerInvariant() };
            promo.State = newState;
            promo.StateChangedAtUtc = nowUtc;
            promo.StateChangedByActorId = callerId;
            promo.IsActive = newState == LifecycleState.Active;
            promo.UpdatedAt = nowUtc;
            var afterP = new { state = promo.State.ToString().ToLowerInvariant() };
            activated = newState == LifecycleState.Active;
            newStateLabel = afterP.state;

            var publishPromoAudit = audit.StageLocal(
                "promotion", promo.Id, "promotion.lifecycle_transitioned",
                callerId, CommercialPermissions.Approver,
                beforeP, afterP,
                diff: new { state_change = new { from = beforeP.state, to = afterP.state }, via = "approval" },
                reasonNote: approvalRow.CosignNote, correlationId: null, nowUtc);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await tx.RollbackAsync(ct);
                return AdminCommercialResponseFactory.Problem(context, 409,
                    CommercialReasonCode.CommercialApprovalAlreadyRecorded,
                    "Another approver has already recorded an approval for this draft.");
            }
            await publishApprovalAudit(ct);
            await publishPromoAudit(ct);
            await tx.CommitAsync(ct);

            if (activated)
            {
                await events.Publish(new PromotionActivated(
                    promo.Id, promo.Name, promo.MarketCodes,
                    promo.StartsAt, promo.EndsAt, nowUtc, callerId), ct);
            }
        }

        return Results.Ok(new RecordApprovalResponse(
            approvalRow.Id, kind, request.TargetEntityId,
            callerId, nowUtc, activated, newStateLabel));
    }

    // -------------------- POST /approvals/reject --------------------

    private static async Task<IResult> RejectApprovalAsync(
        [FromBody] RejectApprovalRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        TimeProvider time,
        CancellationToken ct)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.ReasonNote) ||
            request.ReasonNote.Trim().Length < CosignNoteMinLen)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialApprovalNoteTooShort,
                $"reason_note must be at least {CosignNoteMinLen} characters.");
        }

        var callerId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var nowUtc = time.GetUtcNow();
        var kind = request.TargetEntityKind?.Trim().ToLowerInvariant();
        if (kind != "coupon" && kind != "promotion")
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialApprovalNotPending,
                "Unknown target_entity_kind.");
        }

        // Verify the draft still exists; do not flip its state — the author
        // edits and re-submits (per contract §8.3).
        Guid authorId;
        if (kind == "coupon")
        {
            var coupon = await db.Coupons.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.TargetEntityId, ct);
            if (coupon is null || coupon.State != LifecycleState.Draft)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CommercialApprovalNotPending,
                    "Draft is not pending approval.");
            }
            authorId = coupon.AuthorActorId;
        }
        else
        {
            var promo = await db.Promotions.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == request.TargetEntityId, ct);
            if (promo is null || promo.State != LifecycleState.Draft)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CommercialApprovalNotPending,
                    "Draft is not pending approval.");
            }
            authorId = promo.AuthorActorId;
        }

        if (authorId == callerId)
        {
            return AdminCommercialResponseFactory.Problem(context, 403,
                CommercialReasonCode.CommercialSelfApprovalForbidden,
                "An approver cannot reject their own draft.");
        }

        // Audit-only — no DB row created; rejection is a journal note.
        // CodeRabbit PR #81 round 1 Nit: reuse the already-normalised `kind`
        // variable from above; `kind` is one of {coupon, promotion} by the
        // time we reach this point.
        await audit.AppendAndCommitAsync(
            kind!,
            request.TargetEntityId,
            "commercial.approval_recorded",
            callerId, CommercialPermissions.Approver,
            before: null,
            after: new { outcome = "rejected" },
            diff: null,
            reasonNote: request.ReasonNote.Trim(),
            correlationId: null,
            nowUtc, ct);

        return Results.Ok(new { rejected_at_utc = nowUtc, outcome = "rejected" });
    }

    // -------------------- helpers --------------------

    private static Func<HttpContext, IResult>? ValidateApprovalRequest(RecordApprovalRequest? request)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.TargetEntityKind) ||
            request.TargetEntityId == Guid.Empty)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CommercialApprovalNotPending,
                "target_entity_kind and target_entity_id are required.");
        }
        if (string.IsNullOrWhiteSpace(request.CosignNote) ||
            request.CosignNote.Trim().Length < CosignNoteMinLen)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CommercialApprovalNoteTooShort,
                $"cosign_note must be at least {CosignNoteMinLen} characters.");
        }
        return null;
    }

    private static HighImpactCandidate ToCouponCandidate(Coupon c) => new(
        PercentOff: c.Kind == "percent" ? c.Value / 100m : null,
        AmountOffMinor: c.Kind == "amount" ? c.AmountOffMinor : null,
        CapMinor: c.CapMinor,
        PerCustomerLimit: c.PerCustomerLimit,
        OverallLimit: c.OverallLimit,
        ValidFrom: c.ValidFrom,
        ValidTo: c.ValidTo);

    private static HighImpactCandidate ToPromotionCandidate(Promotion p) => new(
        PercentOff: p.Kind == "percent_off" ? (decimal?)p.PercentOff : null,
        AmountOffMinor: p.Kind == "amount_off" ? p.AmountOffMinor : null,
        CapMinor: null,
        PerCustomerLimit: null,
        OverallLimit: null,
        ValidFrom: p.StartsAt,
        ValidTo: p.EndsAt)
    {
        UsageLimitsApplicable = false,
    };

    private static CommercialThresholdPolicy? ResolveThresholdPolicy(
        IReadOnlyList<CommercialThreshold> thresholds, string[] marketCodes)
    {
        if (marketCodes.Length == 0) return null;
        var keys = marketCodes
            .Select(NormaliseMarketForThreshold)
            .Where(m => m is not null)
            .Select(m => m!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (keys.Count == 0) return null;
        var rows = thresholds.Where(t => keys.Contains(t.MarketCode)).ToList();
        if (rows.Count == 0) return null;
        var enabled = rows.Where(r => r.GateEnabled).ToList();
        if (enabled.Count == 0)
        {
            return new CommercialThresholdPolicy(
                rows[0].MarketCode, GateEnabled: false, null, null, null,
                rows[0].CouponInFlightGraceSeconds, rows[0].PromotionInFlightGraceSeconds);
        }
        return new CommercialThresholdPolicy(
            string.Join(",", enabled.Select(r => r.MarketCode)),
            GateEnabled: true,
            ThresholdPercentOff: enabled.Min(r => r.ThresholdPercentOff),
            ThresholdAmountOffMinor: enabled.Min(r => r.ThresholdAmountOffMinor),
            ThresholdDurationDays: enabled.Min(r => r.ThresholdDurationDays),
            CouponInFlightGraceSeconds: enabled.Min(r => r.CouponInFlightGraceSeconds),
            PromotionInFlightGraceSeconds: enabled.Min(r => r.PromotionInFlightGraceSeconds));
    }

    private static async Task<CommercialThresholdPolicy?> ResolveThresholdAsync(
        PricingDbContext db, string[] marketCodes, CancellationToken ct)
    {
        var thresholds = await db.CommercialThresholds.AsNoTracking().ToListAsync(ct);
        return ResolveThresholdPolicy(thresholds, marketCodes);
    }

    private static string? NormaliseMarketForThreshold(string m) => m?.Trim().ToUpperInvariant() switch
    {
        "SA" or "KSA" => "SA",
        "EG" => "EG",
        _ => null,
    };

    private static string SummariseTrigger(HighImpactCandidate candidate, CommercialThresholdPolicy policy)
    {
        var parts = new List<string>();
        if (policy.ThresholdPercentOff is { } pct && candidate.PercentOff is { } cpct && cpct >= pct)
        {
            parts.Add($"percent_off≥{pct}% (rule={cpct}%)");
        }
        if (policy.ThresholdAmountOffMinor is { } amount)
        {
            var ruling = candidate.CapMinor ?? candidate.AmountOffMinor;
            if (ruling is { } amt && amt >= amount)
            {
                parts.Add($"amount_off_minor≥{amount} (rule={amt})");
            }
        }
        if (candidate.UsageLimitsApplicable &&
            candidate.PerCustomerLimit is null && candidate.OverallLimit is null)
        {
            parts.Add("usage_limits_unset");
        }
        if (policy.ThresholdDurationDays is { } days &&
            candidate.ValidFrom is { } from && candidate.ValidTo is { } to)
        {
            var dur = (to - from).TotalDays;
            if (dur >= days)
            {
                parts.Add($"duration≥{days}d (rule={dur:F0}d)");
            }
        }
        return parts.Count == 0 ? "criteria not enumerated" : string.Join("; ", parts);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
