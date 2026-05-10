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

namespace BackendApi.Modules.Pricing.Admin.Coupons;

/// <summary>
/// Spec 007-b commercial coupon endpoints (US1). Implements contract §2 — nine
/// routes covering create, update, schedule, deactivate, reactivate, clone,
/// list, get, and the always-405 delete (FR-005a).
///
/// Role: every route is gated by <c>commercial.operator</c> (per RBAC table §1);
/// <c>super_admin</c> implicitly satisfies the gate via the platform PolicyEvaluator.
/// </summary>
public static class CommercialCouponEndpoints
{
    private const string ActorRole = CommercialPermissions.Operator;
    private const int CouponCodeMinLen = 3;
    private const int CouponCodeMaxLen = 32;
    private const int LabelMaxLen = 200;
    private const int DescriptionMaxLen = 2000;
    private const int ReasonNoteMinLen = 10;

    public static IEndpointRouteBuilder MapCommercialCouponEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/coupons");
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };

        group.MapGet("", ListAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        group.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        group.MapPost("", CreateAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        group.MapPatch("/{id:guid}", UpdateAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        group.MapPost("/{id:guid}/schedule", ScheduleAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        group.MapPost("/{id:guid}/reactivate", ReactivateAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        group.MapPost("/{id:guid}/clone-as-draft", CloneAsDraftAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        group.MapDelete("/{id:guid}", DeleteForbiddenAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        return builder;
    }

    // -------------------- POST /coupons --------------------

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateCommercialCouponRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        TimeProvider time,
        CancellationToken ct)
    {
        var error = ValidateCreate(request);
        if (error is not null) return error(context);

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var nowUtc = time.GetUtcNow();
        var code = request.Code.Trim().ToUpperInvariant();

        var entity = new Coupon
        {
            Id = Guid.NewGuid(),
            Code = code,
            Kind = MapTypeToKind(request.Type),
            Value = request.Value ?? 0,
            CapMinor = request.CapMinor,
            PerCustomerLimit = request.PerCustomerLimit,
            OverallLimit = request.OverallLimit,
            ExcludesRestricted = request.ExcludesRestricted,
            MarketCodes = NormaliseMarkets(request.Markets),
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            DisplayInBanners = request.DisplayInBanners,
            LabelAr = request.Label!.Ar.Trim(),
            LabelEn = request.Label!.En.Trim(),
            DescriptionAr = string.IsNullOrWhiteSpace(request.Description?.Ar) ? null : request.Description!.Ar.Trim(),
            DescriptionEn = string.IsNullOrWhiteSpace(request.Description?.En) ? null : request.Description!.En.Trim(),
            // 007-b lifecycle starts every authored row in draft (data-model §3.1).
            // Activation goes through POST /schedule, which runs the high-impact gate.
            State = LifecycleState.Draft,
            StateChangedAtUtc = nowUtc,
            StateChangedByActorId = actorId,
            AuthorActorId = actorId,
            // Hold the legacy 007-a IsActive flag in sync with the new lifecycle:
            // engine queries that still inspect IsActive (cart pricing) treat
            // anything not-yet-scheduled as hidden.
            IsActive = false,
            UsedCount = 0,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };

        db.Coupons.Add(entity);

        await audit.AppendAsync(
            "coupon", entity.Id, "coupon.created",
            actorId, ActorRole,
            before: null,
            after: SnapshotForAudit(entity),
            diff: null,
            reasonNote: null,
            correlationId: null,
            nowUtc, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CouponCodeDuplicate,
                "Coupon code already exists.",
                $"Code '{code}' is already taken (case-insensitive).");
        }

        return Results.Created(
            $"/v1/admin/commercial/coupons/{entity.Id:N}",
            ToResponse(entity, auditSummary: null));
    }

    // -------------------- PATCH /coupons/{id} --------------------

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateCommercialCouponRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        TimeProvider time,
        CancellationToken ct)
    {
        var (parsedOk, ifMatch) = AdminCommercialResponseFactory.TryParseIfMatch(context);
        if (!parsedOk)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Malformed If-Match header.");
        }

        var entity = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.row.not_found", "Coupon not found.");
        }

        if (ifMatch is not null && ifMatch.Value != entity.XminRowVersion)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Coupon was changed by another actor; reload before retry.",
                extensions: new Dictionary<string, object?> { ["current"] = ToResponse(entity, null) });
        }

        if (entity.State == LifecycleState.Expired)
        {
            return AdminCommercialResponseFactory.Problem(context, 403,
                CommercialReasonCode.CommercialRowExpiredTerminal,
                "Expired coupons are read-only.");
        }

        // FR-004 active-state pricing-field lock. List of locked fields per
        // contract §2.2: type, value, amount_off_minor, cap_minor, valid_from, valid_to.
        if (entity.State == LifecycleState.Active)
        {
            var pricingFieldTouched =
                request.Type is not null
                || request.Value is not null
                || request.AmountOffMinor is not null
                || request.CapMinor is not null
                || request.ValidFrom is not null
                || request.ValidTo is not null;
            if (pricingFieldTouched)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CouponLockedActivePricingField,
                    "Pricing fields are locked while the coupon is active. Deactivate first.");
            }
        }

        if (request.Markets is not null && request.Markets.Length == 0)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CouponMarketsRequired,
                "At least one market is required.");
        }

        if (request.Label is not null
            && (string.IsNullOrWhiteSpace(request.Label.Ar) || string.IsNullOrWhiteSpace(request.Label.En)))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CouponLabelRequiredBilingual,
                "Both AR and EN labels are required.");
        }

        if (request.ValidFrom is { } vf && request.ValidTo is { } vt && vt <= vf)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CouponScheduleInvalidWindow,
                "valid_to must be strictly after valid_from.");
        }

        if (request.PerCustomerLimit == 0 || request.OverallLimit == 0)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CouponLimitNotZero,
                "Usage limits must be either null (no limit) or > 0; zero is not allowed.");
        }

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var nowUtc = time.GetUtcNow();
        var before = SnapshotForAudit(entity);

        if (request.Type is not null) entity.Kind = MapTypeToKind(request.Type);
        if (request.Value is not null) entity.Value = request.Value.Value;
        if (request.AmountOffMinor is not null) entity.Value = (int)Math.Min(int.MaxValue, request.AmountOffMinor.Value);
        if (request.CapMinor is not null) entity.CapMinor = request.CapMinor;
        if (request.PerCustomerLimit is not null) entity.PerCustomerLimit = request.PerCustomerLimit;
        if (request.OverallLimit is not null) entity.OverallLimit = request.OverallLimit;
        if (request.ExcludesRestricted is not null) entity.ExcludesRestricted = request.ExcludesRestricted.Value;
        if (request.Markets is not null) entity.MarketCodes = NormaliseMarkets(request.Markets);
        if (request.ValidFrom is not null) entity.ValidFrom = request.ValidFrom;
        if (request.ValidTo is not null) entity.ValidTo = request.ValidTo;
        if (request.DisplayInBanners is not null) entity.DisplayInBanners = request.DisplayInBanners.Value;
        if (request.Label is not null)
        {
            entity.LabelAr = request.Label.Ar.Trim();
            entity.LabelEn = request.Label.En.Trim();
        }
        if (request.Description is not null)
        {
            entity.DescriptionAr = string.IsNullOrWhiteSpace(request.Description.Ar) ? null : request.Description.Ar.Trim();
            entity.DescriptionEn = string.IsNullOrWhiteSpace(request.Description.En) ? null : request.Description.En.Trim();
        }
        entity.UpdatedAt = nowUtc;

        var after = SnapshotForAudit(entity);
        await audit.AppendAsync(
            "coupon", entity.Id, "coupon.updated",
            actorId, ActorRole,
            before, after, ComputeDiff(before, after),
            reasonNote: null, correlationId: null, nowUtc, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Coupon was changed by another actor.");
        }

        return Results.Ok(ToResponse(entity, null));
    }

    // -------------------- POST /coupons/{id}/schedule --------------------

    private static async Task<IResult> ScheduleAsync(
        Guid id,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        IPublisher events,
        TimeProvider time,
        CancellationToken ct)
    {
        var (parsedOk, ifMatch) = AdminCommercialResponseFactory.TryParseIfMatch(context);
        if (!parsedOk)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Malformed If-Match header.");
        }

        var entity = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.row.not_found", "Coupon not found.");
        }

        if (ifMatch is not null && ifMatch.Value != entity.XminRowVersion)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Coupon was changed by another actor.");
        }

        if (entity.State != LifecycleState.Draft)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowInvalidTransition,
                $"Cannot schedule from state '{entity.State}'.");
        }

        if (entity.ValidFrom is null || entity.ValidTo is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CouponScheduleInvalidWindow,
                "valid_from and valid_to must be set before scheduling.");
        }
        if (entity.ValidTo <= entity.ValidFrom)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CouponScheduleInvalidWindow,
                "valid_to must be strictly after valid_from.");
        }

        var nowUtc = time.GetUtcNow();

        // High-impact gate (FR-025). Per task T114 the full wiring lands with
        // US5; for US1 we apply the gate here so the contract test for
        // `coupon.activation.requires_approval` is satisfied today.
        var threshold = await ResolveThresholdAsync(db, entity.MarketCodes, ct);
        var candidate = ToHighImpactCandidate(entity);
        if (threshold is not null && HighImpactGate.IsTriggered(candidate, threshold))
        {
            return AdminCommercialResponseFactory.Problem(context, 403,
                CommercialReasonCode.CouponActivationRequiresApproval,
                "This coupon trips the high-impact gate; route to approval queue.",
                extensions: new Dictionary<string, object?>
                {
                    ["market_code"] = threshold.MarketCode,
                    ["author_actor_id"] = entity.AuthorActorId,
                });
        }

        if (!LifecycleStateMachine.TryTransition(
                entity.State, LifecycleTrigger.Schedule, nowUtc,
                entity.ValidFrom, entity.ValidTo, out var newState, out var reason))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                reason ?? CommercialReasonCode.CommercialRowInvalidTransition,
                "Schedule transition rejected.");
        }

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var before = new { state = entity.State.ToString().ToLowerInvariant() };
        entity.State = newState;
        entity.StateChangedAtUtc = nowUtc;
        entity.StateChangedByActorId = actorId;
        entity.IsActive = newState == LifecycleState.Active;
        entity.UpdatedAt = nowUtc;
        var after = new { state = entity.State.ToString().ToLowerInvariant() };

        await audit.AppendAsync(
            "coupon", entity.Id, "coupon.lifecycle_transitioned",
            actorId, ActorRole,
            before, after,
            diff: new { state_change = new { from = before.state, to = after.state } },
            reasonNote: null, correlationId: null, nowUtc, ct);

        await db.SaveChangesAsync(ct);

        if (newState == LifecycleState.Active)
        {
            await events.Publish(new CouponActivated(
                entity.Id, entity.Code, entity.MarketCodes,
                entity.ValidFrom, entity.ValidTo, nowUtc, actorId), ct);
        }

        return Results.Ok(ToResponse(entity, null));
    }

    // -------------------- POST /coupons/{id}/deactivate --------------------

    private static async Task<IResult> DeactivateAsync(
        Guid id,
        [FromBody] DeactivateOrReactivateRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        IPublisher events,
        TimeProvider time,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.ReasonNote) || request.ReasonNote.Trim().Length < ReasonNoteMinLen)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialDeactivationReasonRequired,
                $"reason_note must be at least {ReasonNoteMinLen} characters.");
        }

        var (parsedOk, ifMatch) = AdminCommercialResponseFactory.TryParseIfMatch(context);
        if (!parsedOk)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowVersionConflict, "Malformed If-Match header.");
        }

        var entity = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.row.not_found", "Coupon not found.");
        }

        if (ifMatch is not null && ifMatch.Value != entity.XminRowVersion)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Coupon was changed by another actor.");
        }

        if (!LifecycleStateMachine.TryTransition(
                entity.State, LifecycleTrigger.Deactivate, time.GetUtcNow(),
                entity.ValidFrom, entity.ValidTo, out var newState, out var reason))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                reason ?? CommercialReasonCode.CommercialRowInvalidTransition,
                "Deactivate transition rejected.");
        }

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var nowUtc = time.GetUtcNow();
        var note = request.ReasonNote.Trim();
        var before = new { state = entity.State.ToString().ToLowerInvariant() };
        entity.State = newState;
        entity.StateChangedAtUtc = nowUtc;
        entity.StateChangedByActorId = actorId;
        entity.StateChangedReasonNote = note;
        entity.IsActive = false;
        entity.UpdatedAt = nowUtc;
        var after = new { state = entity.State.ToString().ToLowerInvariant() };

        var threshold = await ResolveThresholdAsync(db, entity.MarketCodes, ct);
        var graceSeconds = threshold?.CouponInFlightGraceSeconds ?? 1800;

        await audit.AppendAsync(
            "coupon", entity.Id, "coupon.lifecycle_transitioned",
            actorId, ActorRole,
            before, after,
            diff: new { state_change = new { from = before.state, to = after.state } },
            reasonNote: note, correlationId: null, nowUtc, ct);

        await db.SaveChangesAsync(ct);

        await events.Publish(new CouponDeactivated(
            entity.Id, nowUtc, actorId, note, graceSeconds), ct);

        return Results.Ok(ToResponse(entity, null));
    }

    // -------------------- POST /coupons/{id}/reactivate --------------------

    private static async Task<IResult> ReactivateAsync(
        Guid id,
        [FromBody] DeactivateOrReactivateRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        IPublisher events,
        TimeProvider time,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.ReasonNote) || request.ReasonNote.Trim().Length < ReasonNoteMinLen)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialDeactivationReasonRequired,
                $"reason_note must be at least {ReasonNoteMinLen} characters.");
        }

        var (parsedOk, ifMatch) = AdminCommercialResponseFactory.TryParseIfMatch(context);
        if (!parsedOk)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowVersionConflict, "Malformed If-Match header.");
        }

        var entity = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.row.not_found", "Coupon not found.");
        }

        if (ifMatch is not null && ifMatch.Value != entity.XminRowVersion)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Coupon was changed by another actor.");
        }

        if (entity.State == LifecycleState.Expired)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialReactivationExpiredTerminal,
                "Expired coupons cannot be reactivated.");
        }

        if (entity.State != LifecycleState.Deactivated)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowInvalidTransition,
                $"Cannot reactivate from state '{entity.State}'.");
        }

        var nowUtc = time.GetUtcNow();
        var threshold = await ResolveThresholdAsync(db, entity.MarketCodes, ct);
        var candidate = ToHighImpactCandidate(entity);
        if (threshold is not null && HighImpactGate.IsTriggered(candidate, threshold))
        {
            return AdminCommercialResponseFactory.Problem(context, 403,
                CommercialReasonCode.CouponActivationRequiresApproval,
                "Reactivation trips the high-impact gate; route to approval queue.");
        }

        if (!LifecycleStateMachine.TryTransition(
                entity.State, LifecycleTrigger.Reactivate, nowUtc,
                entity.ValidFrom, entity.ValidTo, out var newState, out var reason))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                reason ?? CommercialReasonCode.CommercialRowInvalidTransition,
                "Reactivate transition rejected.");
        }

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var note = request.ReasonNote.Trim();
        var before = new { state = entity.State.ToString().ToLowerInvariant() };
        entity.State = newState;
        entity.StateChangedAtUtc = nowUtc;
        entity.StateChangedByActorId = actorId;
        entity.StateChangedReasonNote = note;
        entity.IsActive = newState == LifecycleState.Active;
        entity.UpdatedAt = nowUtc;
        var after = new { state = entity.State.ToString().ToLowerInvariant() };

        await audit.AppendAsync(
            "coupon", entity.Id, "coupon.lifecycle_transitioned",
            actorId, ActorRole,
            before, after,
            diff: new { state_change = new { from = before.state, to = after.state } },
            reasonNote: note, correlationId: null, nowUtc, ct);

        await db.SaveChangesAsync(ct);

        await events.Publish(new CouponReactivated(entity.Id, nowUtc, actorId), ct);

        return Results.Ok(ToResponse(entity, null));
    }

    // -------------------- POST /coupons/{id}/clone-as-draft --------------------

    private static async Task<IResult> CloneAsDraftAsync(
        Guid id,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        TimeProvider time,
        CancellationToken ct)
    {
        var source = await db.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (source is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.row.not_found", "Source coupon not found.");
        }

        if (source.State != LifecycleState.Expired && source.State != LifecycleState.Deactivated)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowInvalidTransition,
                "Clone is allowed only from expired or deactivated coupons.");
        }

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var nowUtc = time.GetUtcNow();
        var draftSuffix = $"_DRAFT_{Guid.NewGuid():N}".Substring(0, 13);
        var clonedCode = (source.Code + draftSuffix).Length > CouponCodeMaxLen
            ? (source.Code.Substring(0, CouponCodeMaxLen - draftSuffix.Length) + draftSuffix)
            : source.Code + draftSuffix;

        var clone = new Coupon
        {
            Id = Guid.NewGuid(),
            Code = clonedCode.ToUpperInvariant(),
            Kind = source.Kind,
            Value = source.Value,
            CapMinor = source.CapMinor,
            PerCustomerLimit = source.PerCustomerLimit,
            OverallLimit = source.OverallLimit,
            ExcludesRestricted = source.ExcludesRestricted,
            MarketCodes = source.MarketCodes,
            ValidFrom = null,
            ValidTo = null,
            DisplayInBanners = source.DisplayInBanners,
            LabelAr = source.LabelAr,
            LabelEn = source.LabelEn,
            DescriptionAr = source.DescriptionAr,
            DescriptionEn = source.DescriptionEn,
            State = LifecycleState.Draft,
            StateChangedAtUtc = nowUtc,
            StateChangedByActorId = actorId,
            AuthorActorId = actorId,
            IsActive = false,
            UsedCount = 0,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };

        db.Coupons.Add(clone);
        await audit.AppendAsync(
            "coupon", clone.Id, "coupon.created",
            actorId, ActorRole,
            before: null,
            after: SnapshotForAudit(clone),
            diff: new { cloned_from = source.Id },
            reasonNote: null, correlationId: null, nowUtc, ct);

        await db.SaveChangesAsync(ct);
        return Results.Created(
            $"/v1/admin/commercial/coupons/{clone.Id:N}",
            ToResponse(clone, null));
    }

    // -------------------- GET /coupons --------------------

    private static async Task<IResult> ListAsync(
        [FromQuery] string? state,
        [FromQuery] string? markets,
        [FromQuery] string? q,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        PricingDbContext db,
        CancellationToken ct)
    {
        var pageSize = limit is null or < 1 ? 50 : Math.Min(limit.Value, 200);

        IQueryable<Coupon> query = db.Coupons.AsNoTracking().Where(c => c.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (Enum.TryParse<LifecycleState>(state, ignoreCase: true, out var stateEnum))
            {
                query = query.Where(c => c.State == stateEnum);
            }
        }
        if (!string.IsNullOrWhiteSpace(markets))
        {
            var marketArr = markets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(m => m.ToLowerInvariant()).ToArray();
            query = query.Where(c => c.MarketCodes.Any(mc => marketArr.Contains(mc)));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pat = $"%{q.Trim().ToUpperInvariant()}%";
            query = query.Where(c =>
                EF.Functions.Like(c.Code, pat) ||
                EF.Functions.ILike(c.LabelEn, pat) ||
                EF.Functions.ILike(c.LabelAr, pat));
        }

        // Cursor-based paging keyed on (CreatedAt, Id) tuple — stable under
        // concurrent inserts. Cursor encoding: "<created_at_ticks>:<id>".
        if (!string.IsNullOrWhiteSpace(cursor) && TryParseCursor(cursor, out var cursorAt, out var cursorId))
        {
            query = query.Where(c =>
                c.CreatedAt < cursorAt ||
                (c.CreatedAt == cursorAt && c.Id.CompareTo(cursorId) < 0));
        }

        var rows = await query
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > pageSize)
        {
            var last = rows[pageSize - 1];
            nextCursor = $"{last.CreatedAt.UtcTicks}:{last.Id:N}";
            rows = rows.Take(pageSize).ToList();
        }

        return Results.Ok(new CommercialCouponListResponse(
            rows.Select(c => ToResponse(c, null)).ToArray(),
            nextCursor));
    }

    // -------------------- GET /coupons/{id} --------------------

    private static async Task<IResult> GetAsync(
        Guid id,
        PricingDbContext db,
        CancellationToken ct)
    {
        var entity = await db.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return Results.NotFound();
        }

        var summary = await db.CommercialAuditEvents.AsNoTracking()
            .Where(e => e.TargetEntityKind == "coupon" && e.TargetEntityId == id)
            .OrderByDescending(e => e.RecordedAtUtc)
            .Take(10)
            .Select(e => new CommercialAuditSummaryRow(e.Kind, e.ActorId, e.ActorRole, e.RecordedAtUtc, e.ReasonNote))
            .ToListAsync(ct);

        return Results.Ok(ToResponse(entity, summary));
    }

    // -------------------- DELETE /coupons/{id} --------------------

    /// <summary>
    /// FR-005a — hard-delete on coupons is forbidden by the constitution
    /// (Principle 25 / soft-only data retention). Returns 405 with the
    /// canonical reason code so admin UI / API clients can surface the
    /// "use deactivate or clone-as-draft" affordance instead.
    /// </summary>
    private static IResult DeleteForbiddenAsync(Guid id, HttpContext context)
    {
        return AdminCommercialResponseFactory.Problem(context, 405,
            CommercialReasonCode.CommercialRowDeleteForbidden,
            "Hard-delete is forbidden; deactivate or clone-as-draft instead.");
    }

    // -------------------- helpers --------------------

    private static Func<HttpContext, IResult>? ValidateCreate(CreateCommercialCouponRequest? request)
    {
        if (request is null)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CouponLabelRequiredBilingual, "Request body is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Code) ||
            request.Code.Trim().Length < CouponCodeMinLen ||
            request.Code.Trim().Length > CouponCodeMaxLen)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CouponValueOutOfRange,
                $"Code length must be between {CouponCodeMinLen} and {CouponCodeMaxLen} characters.");
        }
        if (request.Markets is null || request.Markets.Length == 0)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CouponMarketsRequired,
                "At least one market is required.");
        }
        if (request.Type is null
            || (request.Type != "percent_off" && request.Type != "amount_off"))
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CouponValueOutOfRange,
                "Type must be 'percent_off' or 'amount_off'.");
        }
        if (request.Type == "percent_off")
        {
            if (request.Value is null or <= 0 or > 100)
            {
                return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                    CommercialReasonCode.CouponValueOutOfRange,
                    "Percent_off requires value in (0, 100].");
            }
        }
        if (request.Type == "amount_off" && (request.AmountOffMinor is null or <= 0))
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CouponValueOutOfRange,
                "Amount_off requires amount_off_minor > 0.");
        }
        if (request.Label is null
            || string.IsNullOrWhiteSpace(request.Label.Ar)
            || string.IsNullOrWhiteSpace(request.Label.En))
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CouponLabelRequiredBilingual,
                "Both AR and EN labels are required.");
        }
        if (request.Label.Ar.Length > LabelMaxLen || request.Label.En.Length > LabelMaxLen)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CommercialTextTooLong,
                $"Label exceeds {LabelMaxLen} characters.");
        }
        if (request.Description is not null
            && ((request.Description.Ar?.Length ?? 0) > DescriptionMaxLen
                || (request.Description.En?.Length ?? 0) > DescriptionMaxLen))
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CommercialTextTooLong,
                $"Description exceeds {DescriptionMaxLen} characters.");
        }
        if (request.PerCustomerLimit == 0 || request.OverallLimit == 0)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CouponLimitNotZero,
                "Usage limits must be either null (no limit) or > 0; zero is not allowed.");
        }
        if (request.ValidFrom is { } vf && request.ValidTo is { } vt && vt <= vf)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CouponScheduleInvalidWindow,
                "valid_to must be strictly after valid_from.");
        }
        return null;
    }

    private static string MapTypeToKind(string? type) => type switch
    {
        "percent_off" => "percent",
        "amount_off" => "amount",
        // Map the persisted 007-a kind values back when reading;
        // upper layers always normalise on type.
        "percent" => "percent",
        "amount" => "amount",
        _ => "percent",
    };

    private static string MapKindToType(string kind) => kind switch
    {
        "percent" => "percent_off",
        "amount" => "amount_off",
        _ => kind,
    };

    private static string[] NormaliseMarkets(string[] raw) =>
        raw.Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool TryParseCursor(string cursor, out DateTimeOffset at, out Guid id)
    {
        at = default;
        id = default;
        var parts = cursor.Split(':');
        if (parts.Length != 2) return false;
        if (!long.TryParse(parts[0], out var ticks)) return false;
        if (!Guid.TryParse(parts[1], out id)) return false;
        at = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }

    private static async Task<CommercialThresholdPolicy?> ResolveThresholdAsync(
        PricingDbContext db, string[] marketCodes, CancellationToken ct)
    {
        if (marketCodes.Length == 0) return null;

        // Threshold rows are keyed by uppercase market codes (SA / EG)
        // while cart-pricing market codes are lowercase (ksa / eg).
        // Normalise both directions so the gate fires regardless of caller convention.
        var upper = marketCodes.Select(NormaliseMarketForThreshold).Where(m => m is not null).ToArray();
        if (upper.Length == 0) return null;

        var rows = await db.CommercialThresholds.AsNoTracking()
            .Where(t => upper.Contains(t.MarketCode))
            .ToListAsync(ct);
        if (rows.Count == 0) return null;

        // Multi-market coupon: the most-restrictive threshold wins (lowest pct,
        // lowest amount, shortest duration). Gate-disabled rows abstain by
        // contributing null criteria so other rows can still trip the gate.
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
            ThresholdPercentOff: enabled.Where(r => r.ThresholdPercentOff is not null).Select(r => r.ThresholdPercentOff!.Value).DefaultIfEmpty().Min(),
            ThresholdAmountOffMinor: enabled.Where(r => r.ThresholdAmountOffMinor is not null).Select(r => r.ThresholdAmountOffMinor!.Value).DefaultIfEmpty().Min(),
            ThresholdDurationDays: enabled.Where(r => r.ThresholdDurationDays is not null).Select(r => r.ThresholdDurationDays!.Value).DefaultIfEmpty().Min(),
            CouponInFlightGraceSeconds: enabled.Min(r => r.CouponInFlightGraceSeconds),
            PromotionInFlightGraceSeconds: enabled.Min(r => r.PromotionInFlightGraceSeconds));
    }

    /// <summary>
    /// Translate the cart-pricing market code (lowercase 'ksa' / 'eg') into the
    /// commercial_thresholds key (uppercase 'SA' / 'EG'). Returns null for
    /// unknown codes so the gate abstains rather than hard-fails.
    /// </summary>
    private static string? NormaliseMarketForThreshold(string m) => m?.Trim().ToUpperInvariant() switch
    {
        "SA" or "KSA" => "SA",
        "EG" => "EG",
        _ => null,
    };

    private static HighImpactCandidate ToHighImpactCandidate(Coupon c) => new(
        PercentOff: c.Kind == "percent" ? c.Value / 100m : null,
        AmountOffMinor: c.Kind == "amount" ? c.Value : null,
        CapMinor: c.CapMinor,
        PerCustomerLimit: c.PerCustomerLimit,
        OverallLimit: c.OverallLimit,
        ValidFrom: c.ValidFrom,
        ValidTo: c.ValidTo);

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static object SnapshotForAudit(Coupon c) => new
    {
        c.Id,
        c.Code,
        c.Kind,
        c.Value,
        c.CapMinor,
        c.PerCustomerLimit,
        c.OverallLimit,
        c.ExcludesRestricted,
        markets = c.MarketCodes,
        c.ValidFrom,
        c.ValidTo,
        c.DisplayInBanners,
        label = new { ar = c.LabelAr, en = c.LabelEn },
        description = c.DescriptionAr is null && c.DescriptionEn is null ? null : new { ar = c.DescriptionAr, en = c.DescriptionEn },
        state = c.State.ToString().ToLowerInvariant(),
    };

    private static object ComputeDiff(object before, object after)
    {
        // Lightweight reflection-based diff for audit body. JsonSerializer
        // already produced flat property bags above; here we just stash
        // both snapshots so SC-003 audit-coverage scripts can compute the
        // field-level delta against a stable schema. A richer JSONPath diff
        // can land in the Polish phase if SC-003 demands it.
        return new { before, after };
    }

    private static CommercialCouponResponse ToResponse(Coupon c, IReadOnlyList<CommercialAuditSummaryRow>? auditSummary) =>
        new(
            Id: c.Id,
            Code: c.Code,
            State: c.State.ToString().ToLowerInvariant(),
            Type: MapKindToType(c.Kind),
            Value: c.Kind == "percent" ? c.Value : null,
            CapMinor: c.CapMinor,
            PerCustomerLimit: c.PerCustomerLimit,
            OverallLimit: c.OverallLimit,
            ExcludesRestricted: c.ExcludesRestricted,
            Markets: c.MarketCodes,
            ValidFrom: c.ValidFrom,
            ValidTo: c.ValidTo,
            DisplayInBanners: c.DisplayInBanners,
            Label: new CommercialCouponBilingual(c.LabelAr, c.LabelEn),
            Description: c.DescriptionAr is null && c.DescriptionEn is null
                ? null
                : new CommercialCouponBilingual(c.DescriptionAr ?? string.Empty, c.DescriptionEn ?? string.Empty),
            RowVersion: c.XminRowVersion,
            CreatedAtUtc: c.CreatedAt,
            StateChangedAtUtc: c.StateChangedAtUtc,
            AuthorActorId: c.AuthorActorId,
            AuditSummary: auditSummary);
}
