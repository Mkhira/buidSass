using System.Text.Json;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Caches;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Pricing.Admin.Promotions;

/// <summary>
/// Spec 007-b commercial promotion endpoints (US2). Implements contract §3 —
/// nine routes mirroring US1's coupon surface: create, update, schedule (with
/// SKU-overlap warning + ack loop), deactivate, reactivate, clone, list, get,
/// and always-405 delete (FR-005a).
///
/// Role: every route is gated by <c>commercial.operator</c> (per RBAC §1);
/// <c>super_admin</c> implicitly satisfies the gate via the platform
/// PolicyEvaluator. The legacy 007-a admin endpoints under
/// <c>/v1/admin/pricing/promotions</c> remain mounted alongside this surface
/// for backwards compatibility.
/// </summary>
public static class CommercialPromotionEndpoints
{
    private const string ActorRole = CommercialPermissions.Operator;
    private const int LabelMaxLen = 200;
    private const int DescriptionMaxLen = 2000;
    private const int ReasonNoteMinLen = 10;
    private const int AppliesToMaxCount = 500;

    public static IEndpointRouteBuilder MapCommercialPromotionEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/promotions");
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

    // -------------------- POST /promotions --------------------

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateCommercialPromotionRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        PromotionCache cache,
        TimeProvider time,
        CancellationToken ct)
    {
        var error = ValidateCreate(request);
        if (error is not null) return error(context);

        var kind = NormaliseKind(request.Kind);
        var normalisedMarkets = NormaliseMarkets(request.Markets);
        if (normalisedMarkets.Length == 0)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CouponMarketsRequired,
                "At least one non-blank market is required.");
        }

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var nowUtc = time.GetUtcNow();

        var entity = new Promotion
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Name = request.Label!.En.Trim(),  // legacy 007-a Name column mirrors EN label
            ConfigJson = BuildConfigJson(kind, request.PercentOff, request.AmountOffMinor,
                request.RewardSku, request.BundleSku),
            AppliesToProductIds = request.AppliesToProductIds,
            AppliesToCategoryIds = request.AppliesToCategoryIds,
            MarketCodes = normalisedMarkets,
            Priority = request.Priority,
            StartsAt = request.ValidFrom,
            EndsAt = request.ValidTo,
            // 007-b lifecycle starts every authored row in draft (data-model §3.1).
            State = LifecycleState.Draft,
            StateChangedAtUtc = nowUtc,
            StateChangedByActorId = actorId,
            AuthorActorId = actorId,
            // Hold the legacy 007-a IsActive flag in sync with the new lifecycle:
            // engine queries that still inspect IsActive treat anything not-yet-scheduled as hidden.
            IsActive = false,
            BannerEligible = request.BannerEligible,
            // Bilingual labels (Principle 4 / contract §3).
            LabelAr = request.Label!.Ar.Trim(),
            LabelEn = request.Label!.En.Trim(),
            DescriptionAr = string.IsNullOrWhiteSpace(request.Description?.Ar) ? null : request.Description!.Ar.Trim(),
            DescriptionEn = string.IsNullOrWhiteSpace(request.Description?.En) ? null : request.Description!.En.Trim(),
            // Pricing-field column shadow (engine still reads ConfigJson; these
            // columns make the authored values explicit for audit/admin).
            PercentOff = kind == "percent_off" ? request.PercentOff : null,
            AmountOffMinor = kind == "amount_off" ? request.AmountOffMinor : null,
            RewardSku = kind == "bogo" ? request.RewardSku : null,
            BundleSku = kind == "bundle" ? request.BundleSku : null,
            StacksWithCoupons = request.StacksWithCoupons,
            StacksWithOtherPromotions = request.StacksWithOtherPromotions,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };

        db.Promotions.Add(entity);

        // Stage the dual-audit row (mirrors US1 coupon pattern).
        var publishAudit = audit.StageLocal(
            "promotion", entity.Id, "promotion.created",
            actorId, ActorRole,
            before: null,
            after: SnapshotForAudit(entity),
            diff: null,
            reasonNote: null,
            correlationId: null,
            nowUtc);

        await db.SaveChangesAsync(ct);
        await publishAudit(ct);
        cache.Invalidate();

        return Results.Created(
            $"/v1/admin/commercial/promotions/{entity.Id:N}",
            ToResponse(entity, auditSummary: null));
    }

    // -------------------- PATCH /promotions/{id} --------------------

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateCommercialPromotionRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        PromotionCache cache,
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

        var entity = await db.Promotions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.row.not_found", "Promotion not found.");
        }

        if (ifMatch is not null && ifMatch.Value != entity.XminRowVersion)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Promotion was changed by another actor; reload before retry.",
                extensions: new Dictionary<string, object?> { ["current"] = ToResponse(entity, null) });
        }

        if (entity.State == LifecycleState.Expired)
        {
            return AdminCommercialResponseFactory.Problem(context, 403,
                CommercialReasonCode.CommercialRowExpiredTerminal,
                "Expired promotions are read-only.");
        }

        // FR-004 active-state pricing-field lock — contract §3 names:
        // kind, percent_off, amount_off_minor, reward_sku, bundle_sku, valid_from, valid_to,
        // priority, applies_to_*.
        if (entity.State == LifecycleState.Active)
        {
            var pricingFieldTouched =
                request.Kind is not null
                || request.PercentOff is not null
                || request.AmountOffMinor is not null
                || request.RewardSku is not null
                || request.BundleSku is not null
                || request.ValidFrom is not null
                || request.ValidTo is not null
                || request.Priority is not null
                || request.AppliesToProductIds is not null
                || request.AppliesToCategoryIds is not null;
            if (pricingFieldTouched)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.PromotionLockedActivePricingField,
                    "Pricing fields are locked while the promotion is active. Deactivate first.");
            }
        }

        string[]? normalisedMarketsForUpdate = null;
        if (request.Markets is not null)
        {
            normalisedMarketsForUpdate = NormaliseMarkets(request.Markets);
            if (normalisedMarketsForUpdate.Length == 0)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CouponMarketsRequired,
                    "At least one non-blank market is required.");
            }
        }

        if (request.Label is not null
            && (string.IsNullOrWhiteSpace(request.Label.Ar) || string.IsNullOrWhiteSpace(request.Label.En)))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.PromotionLabelRequiredBilingual,
                "Both AR and EN labels are required.");
        }

        if (request.ValidFrom is { } vf && request.ValidTo is { } vt && vt <= vf)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.PromotionScheduleInvalidWindow,
                "valid_to must be strictly after valid_from.");
        }

        // applies_to too-many guard (FR / contract §3).
        var newProductIds = request.AppliesToProductIds ?? entity.AppliesToProductIds;
        if (newProductIds is { Length: > AppliesToMaxCount })
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.PromotionAppliesToTooMany,
                $"applies_to_product_ids exceeds {AppliesToMaxCount} entries.");
        }

        // Kind change: validate the new kind's required pricing field.
        string? patchedKind = null;
        if (request.Kind is not null)
        {
            patchedKind = NormaliseKind(request.Kind);
            if (patchedKind == "percent_off" && request.PercentOff is null && entity.PercentOff is null)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CouponValueOutOfRange,
                    "percent_off requires a value when kind is percent_off.");
            }
        }
        var effectiveKind = patchedKind ?? entity.Kind;

        if (request.PercentOff is { } pct)
        {
            if (effectiveKind != "percent_off")
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CouponValueOutOfRange,
                    "percent_off is only valid when kind is percent_off.");
            }
            if (pct <= 0 || pct > 100)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CouponValueOutOfRange,
                    "percent_off must be in (0, 100].");
            }
        }
        if (request.AmountOffMinor is { } amt && amt <= 0)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CouponValueOutOfRange,
                "amount_off_minor must be > 0.");
        }

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var nowUtc = time.GetUtcNow();
        var before = SnapshotForAudit(entity);

        if (patchedKind is not null) entity.Kind = patchedKind;
        if (request.PercentOff is not null && effectiveKind == "percent_off")
            entity.PercentOff = request.PercentOff;
        if (request.AmountOffMinor is not null && effectiveKind == "amount_off")
            entity.AmountOffMinor = request.AmountOffMinor;
        if (request.RewardSku is not null) entity.RewardSku = request.RewardSku;
        if (request.BundleSku is not null) entity.BundleSku = request.BundleSku;
        if (request.AppliesToProductIds is not null)
            entity.AppliesToProductIds = request.AppliesToProductIds;
        if (request.AppliesToCategoryIds is not null)
            entity.AppliesToCategoryIds = request.AppliesToCategoryIds;
        if (normalisedMarketsForUpdate is not null) entity.MarketCodes = normalisedMarketsForUpdate;
        if (request.Priority is { } p) entity.Priority = p;
        if (request.ValidFrom is not null) entity.StartsAt = request.ValidFrom;
        if (request.ValidTo is not null) entity.EndsAt = request.ValidTo;
        if (request.StacksWithCoupons is not null) entity.StacksWithCoupons = request.StacksWithCoupons.Value;
        if (request.StacksWithOtherPromotions is not null) entity.StacksWithOtherPromotions = request.StacksWithOtherPromotions.Value;
        if (request.BannerEligible is not null) entity.BannerEligible = request.BannerEligible.Value;
        if (request.Label is not null)
        {
            entity.LabelAr = request.Label.Ar.Trim();
            entity.LabelEn = request.Label.En.Trim();
            entity.Name = request.Label.En.Trim();  // keep legacy Name in sync
        }
        if (request.Description is not null)
        {
            entity.DescriptionAr = string.IsNullOrWhiteSpace(request.Description.Ar) ? null : request.Description.Ar.Trim();
            entity.DescriptionEn = string.IsNullOrWhiteSpace(request.Description.En) ? null : request.Description.En.Trim();
        }
        // Rebuild ConfigJson so the engine sees the patched pricing values.
        entity.ConfigJson = BuildConfigJson(
            entity.Kind, entity.PercentOff, entity.AmountOffMinor,
            entity.RewardSku, entity.BundleSku);
        entity.UpdatedAt = nowUtc;

        var after = SnapshotForAudit(entity);
        var publishAudit = audit.StageLocal(
            "promotion", entity.Id, "promotion.updated",
            actorId, ActorRole,
            before, after, diff: null,
            reasonNote: null, correlationId: null, nowUtc);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Promotion was changed by another actor.");
        }
        await publishAudit(ct);
        cache.Invalidate();

        return Results.Ok(ToResponse(entity, null));
    }

    // -------------------- POST /promotions/{id}/schedule --------------------

    private static async Task<IResult> ScheduleAsync(
        Guid id,
        [FromBody] CommercialPromotionScheduleRequest? request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        PromotionCache cache,
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

        var entity = await db.Promotions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.row.not_found", "Promotion not found.");
        }

        if (ifMatch is not null && ifMatch.Value != entity.XminRowVersion)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Promotion was changed by another actor.");
        }

        if (entity.State != LifecycleState.Draft)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowInvalidTransition,
                $"Cannot schedule from state '{entity.State}'.");
        }

        if (entity.StartsAt is null || entity.EndsAt is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.PromotionScheduleInvalidWindow,
                "valid_from and valid_to must be set before scheduling.");
        }
        if (entity.EndsAt <= entity.StartsAt)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.PromotionScheduleInvalidWindow,
                "valid_to must be strictly after valid_from.");
        }

        // BOGO / bundle target-SKU validation per contract §3 (FR-016 mirror).
        // Both must reference a non-archived SKU. At this layer we only catch
        // the "required" half; cross-module archive checks land in Polish via
        // CatalogSkuArchivedHandler.
        if (entity.Kind == "bogo" && entity.RewardSku is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.PromotionTargetSkuInvalid,
                "BOGO promotions require reward_sku.");
        }
        if (entity.Kind == "bundle" && entity.BundleSku is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.PromotionTargetSkuInvalid,
                "Bundle promotions require bundle_sku.");
        }

        // SKU-overlap warning loop (FR-016 / contract §3). When this promotion
        // does not stack with other promotions and SKU overlap exists, the first
        // schedule call returns 400 with overlapping ids; client re-posts with
        // acknowledge_overlap=true.
        if (!entity.StacksWithOtherPromotions)
        {
            var overlapping = await FindOverlappingPromotionsAsync(db, entity, ct);
            if (overlapping.Count > 0 && request?.AcknowledgeOverlap != true)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.PromotionOverlapWarning,
                    "Promotion overlaps with other active or scheduled promotions on the same SKUs.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["overlapping_rule_ids"] = overlapping,
                    });
            }
        }

        var nowUtc = time.GetUtcNow();

        // High-impact gate (FR-025). Mirror of US1 coupon schedule wiring.
        var threshold = await ResolveThresholdAsync(db, entity.MarketCodes, ct);
        var candidate = ToHighImpactCandidate(entity);
        if (threshold is not null && HighImpactGate.IsTriggered(candidate, threshold))
        {
            return AdminCommercialResponseFactory.Problem(context, 403,
                CommercialReasonCode.PromotionActivationRequiresApproval,
                "This promotion trips the high-impact gate; route to approval queue.",
                extensions: new Dictionary<string, object?>
                {
                    ["market_code"] = threshold.MarketCode,
                    ["author_actor_id"] = entity.AuthorActorId,
                });
        }

        if (!LifecycleStateMachine.TryTransition(
                entity.State, LifecycleTrigger.Schedule, nowUtc,
                entity.StartsAt, entity.EndsAt, out var newState, out var reason))
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

        var publishAudit = audit.StageLocal(
            "promotion", entity.Id, "promotion.lifecycle_transitioned",
            actorId, ActorRole,
            before, after,
            diff: new { state_change = new { from = before.state, to = after.state } },
            reasonNote: null, correlationId: null, nowUtc);

        await db.SaveChangesAsync(ct);
        await publishAudit(ct);
        cache.Invalidate();

        if (newState == LifecycleState.Active)
        {
            await events.Publish(new PromotionActivated(
                entity.Id, entity.LabelEn, entity.MarketCodes,
                entity.StartsAt, entity.EndsAt, nowUtc, actorId), ct);
        }

        return Results.Ok(ToResponse(entity, null));
    }

    // -------------------- POST /promotions/{id}/deactivate --------------------

    private static async Task<IResult> DeactivateAsync(
        Guid id,
        [FromBody] DeactivateOrReactivatePromotionRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        PromotionCache cache,
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

        var entity = await db.Promotions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.row.not_found", "Promotion not found.");
        }

        if (ifMatch is not null && ifMatch.Value != entity.XminRowVersion)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Promotion was changed by another actor.");
        }

        if (!LifecycleStateMachine.TryTransition(
                entity.State, LifecycleTrigger.Deactivate, time.GetUtcNow(),
                entity.StartsAt, entity.EndsAt, out var newState, out var reason))
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
        var graceSeconds = threshold?.PromotionInFlightGraceSeconds ?? 1800;

        var publishAudit = audit.StageLocal(
            "promotion", entity.Id, "promotion.lifecycle_transitioned",
            actorId, ActorRole,
            before, after,
            diff: new { state_change = new { from = before.state, to = after.state } },
            reasonNote: note, correlationId: null, nowUtc);

        await db.SaveChangesAsync(ct);
        await publishAudit(ct);
        cache.Invalidate();

        await events.Publish(new PromotionDeactivated(
            entity.Id, nowUtc, actorId, note, graceSeconds), ct);

        return Results.Ok(ToResponse(entity, null));
    }

    // -------------------- POST /promotions/{id}/reactivate --------------------

    private static async Task<IResult> ReactivateAsync(
        Guid id,
        [FromBody] DeactivateOrReactivatePromotionRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        PromotionCache cache,
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

        var entity = await db.Promotions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.row.not_found", "Promotion not found.");
        }

        if (ifMatch is not null && ifMatch.Value != entity.XminRowVersion)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Promotion was changed by another actor.");
        }

        if (entity.State == LifecycleState.Expired)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialReactivationExpiredTerminal,
                "Expired promotions cannot be reactivated.");
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
                CommercialReasonCode.PromotionActivationRequiresApproval,
                "Reactivation trips the high-impact gate; route to approval queue.");
        }

        if (!LifecycleStateMachine.TryTransition(
                entity.State, LifecycleTrigger.Reactivate, nowUtc,
                entity.StartsAt, entity.EndsAt, out var newState, out var reason))
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

        var publishAudit = audit.StageLocal(
            "promotion", entity.Id, "promotion.lifecycle_transitioned",
            actorId, ActorRole,
            before, after,
            diff: new { state_change = new { from = before.state, to = after.state } },
            reasonNote: note, correlationId: null, nowUtc);

        await db.SaveChangesAsync(ct);
        await publishAudit(ct);
        cache.Invalidate();

        await events.Publish(new PromotionReactivated(entity.Id, nowUtc, actorId), ct);

        return Results.Ok(ToResponse(entity, null));
    }

    // -------------------- POST /promotions/{id}/clone-as-draft --------------------

    private static async Task<IResult> CloneAsDraftAsync(
        Guid id,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        PromotionCache cache,
        TimeProvider time,
        CancellationToken ct)
    {
        var source = await db.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (source is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.row.not_found", "Source promotion not found.");
        }

        if (source.State != LifecycleState.Expired && source.State != LifecycleState.Deactivated)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowInvalidTransition,
                "Clone is allowed only from expired or deactivated promotions.");
        }

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var nowUtc = time.GetUtcNow();

        var clone = new Promotion
        {
            Id = Guid.NewGuid(),
            Kind = source.Kind,
            Name = source.Name,
            ConfigJson = source.ConfigJson,
            AppliesToProductIds = source.AppliesToProductIds,
            AppliesToCategoryIds = source.AppliesToCategoryIds,
            MarketCodes = source.MarketCodes,
            Priority = source.Priority,
            StartsAt = null,
            EndsAt = null,
            State = LifecycleState.Draft,
            StateChangedAtUtc = nowUtc,
            StateChangedByActorId = actorId,
            AuthorActorId = actorId,
            IsActive = false,
            BannerEligible = source.BannerEligible,
            LabelAr = source.LabelAr,
            LabelEn = source.LabelEn,
            DescriptionAr = source.DescriptionAr,
            DescriptionEn = source.DescriptionEn,
            PercentOff = source.PercentOff,
            AmountOffMinor = source.AmountOffMinor,
            RewardSku = source.RewardSku,
            BundleSku = source.BundleSku,
            StacksWithCoupons = source.StacksWithCoupons,
            StacksWithOtherPromotions = source.StacksWithOtherPromotions,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };

        db.Promotions.Add(clone);
        var publishAudit = audit.StageLocal(
            "promotion", clone.Id, "promotion.created",
            actorId, ActorRole,
            before: null,
            after: SnapshotForAudit(clone),
            diff: new { cloned_from = source.Id },
            reasonNote: null, correlationId: null, nowUtc);

        await db.SaveChangesAsync(ct);
        await publishAudit(ct);
        cache.Invalidate();
        return Results.Created(
            $"/v1/admin/commercial/promotions/{clone.Id:N}",
            ToResponse(clone, null));
    }

    // -------------------- GET /promotions --------------------

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

        IQueryable<Promotion> query = db.Promotions.AsNoTracking().Where(p => p.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (Enum.TryParse<LifecycleState>(state, ignoreCase: true, out var stateEnum))
            {
                query = query.Where(p => p.State == stateEnum);
            }
        }
        if (!string.IsNullOrWhiteSpace(markets))
        {
            var marketArr = markets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(m => m.ToLowerInvariant()).ToArray();
            query = query.Where(p => p.MarketCodes.Any(mc => marketArr.Contains(mc)));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pat = $"%{q.Trim()}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.LabelEn, pat) ||
                EF.Functions.ILike(p.LabelAr, pat));
        }

        if (!string.IsNullOrWhiteSpace(cursor) && TryParseCursor(cursor, out var cursorAt, out var cursorId))
        {
            query = query.Where(p =>
                p.CreatedAt < cursorAt ||
                (p.CreatedAt == cursorAt && p.Id.CompareTo(cursorId) < 0));
        }

        var rows = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > pageSize)
        {
            var last = rows[pageSize - 1];
            nextCursor = $"{last.CreatedAt.UtcTicks}:{last.Id:N}";
            rows = rows.Take(pageSize).ToList();
        }

        return Results.Ok(new CommercialPromotionListResponse(
            rows.Select(p => ToResponse(p, null)).ToArray(),
            nextCursor));
    }

    // -------------------- GET /promotions/{id} --------------------

    private static async Task<IResult> GetAsync(
        Guid id,
        PricingDbContext db,
        CancellationToken ct)
    {
        var entity = await db.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
        {
            return Results.NotFound();
        }

        var summary = await db.CommercialAuditEvents.AsNoTracking()
            .Where(e => e.TargetEntityKind == "promotion" && e.TargetEntityId == id)
            .OrderByDescending(e => e.RecordedAtUtc)
            .Take(10)
            .Select(e => new CommercialPromotionAuditSummaryRow(e.Kind, e.ActorId, e.ActorRole, e.RecordedAtUtc, e.ReasonNote))
            .ToListAsync(ct);

        return Results.Ok(ToResponse(entity, summary));
    }

    // -------------------- DELETE /promotions/{id} --------------------

    /// <summary>
    /// FR-005a — hard-delete on promotions is forbidden by the constitution
    /// (Principle 25 / soft-only data retention). Returns 405 with the canonical
    /// reason code so admin UI / API clients can surface the "deactivate or
    /// clone-as-draft" affordance instead.
    /// </summary>
    private static IResult DeleteForbiddenAsync(Guid id, HttpContext context)
    {
        return AdminCommercialResponseFactory.Problem(context, 405,
            CommercialReasonCode.CommercialRowDeleteForbidden,
            "Hard-delete is forbidden; deactivate or clone-as-draft instead.");
    }

    // -------------------- helpers --------------------

    private static Func<HttpContext, IResult>? ValidateCreate(CreateCommercialPromotionRequest? request)
    {
        if (request is null)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.PromotionLabelRequiredBilingual, "Request body is required.");
        }
        var kind = NormaliseKind(request.Kind);
        if (kind is not ("percent_off" or "amount_off" or "bogo" or "bundle"))
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CouponValueOutOfRange,
                "Kind must be one of: percent_off, amount_off, bogo, bundle.");
        }
        if (request.Markets is null || request.Markets.Length == 0)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CouponMarketsRequired,
                "At least one market is required.");
        }
        if (kind == "percent_off")
        {
            if (request.PercentOff is null or <= 0 or > 100)
            {
                return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                    CommercialReasonCode.CouponValueOutOfRange,
                    "percent_off requires a value in (0, 100].");
            }
        }
        if (kind == "amount_off" && (request.AmountOffMinor is null or <= 0))
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CouponValueOutOfRange,
                "amount_off requires amount_off_minor > 0.");
        }
        if (kind == "bogo" && request.RewardSku is null)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.PromotionTargetSkuInvalid,
                "BOGO requires reward_sku.");
        }
        if (kind == "bundle" && request.BundleSku is null)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.PromotionTargetSkuInvalid,
                "Bundle requires bundle_sku.");
        }
        if (request.AppliesToProductIds is { Length: > AppliesToMaxCount })
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.PromotionAppliesToTooMany,
                $"applies_to_product_ids exceeds {AppliesToMaxCount} entries.");
        }
        if (request.Label is null
            || string.IsNullOrWhiteSpace(request.Label.Ar)
            || string.IsNullOrWhiteSpace(request.Label.En))
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.PromotionLabelRequiredBilingual,
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
        if (request.ValidFrom is { } vf && request.ValidTo is { } vt && vt <= vf)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.PromotionScheduleInvalidWindow,
                "valid_to must be strictly after valid_from.");
        }
        return null;
    }

    private static string NormaliseKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "percent_off" => "percent_off",
        "amount_off" => "amount_off",
        "bogo" => "bogo",
        "bundle" => "bundle",
        "bundle_wrapper" => "bundle",
        _ => kind?.Trim().ToLowerInvariant() ?? string.Empty,
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

    /// <summary>
    /// Build the legacy 007-a ConfigJson blob from the authored typed fields so
    /// the cart pricing engine (<see cref="PromotionCache.MapToSnapshot"/>) keeps
    /// resolving values without modification (Principle 10 / engine immutability).
    /// </summary>
    private static string BuildConfigJson(string kind, int? percentOff, long? amountOffMinor,
        Guid? rewardSku, Guid? bundleSku)
    {
        var doc = new Dictionary<string, object?>();
        switch (kind)
        {
            case "percent_off":
                if (percentOff is int pct) doc["percentBps"] = pct * 100;
                break;
            case "amount_off":
                if (amountOffMinor is long amt) doc["amountMinor"] = amt;
                break;
            case "bogo":
                if (rewardSku is Guid r)
                {
                    doc["qualifyingProductId"] = r.ToString();
                    doc["rewardProductId"] = r.ToString();
                    doc["qualifyQty"] = 1;
                    doc["rewardQty"] = 1;
                    doc["rewardPercentBps"] = 10_000;  // free reward
                }
                break;
            case "bundle":
                if (bundleSku is Guid b) doc["bundleProductId"] = b.ToString();
                break;
        }
        return JsonSerializer.Serialize(doc);
    }

    /// <summary>
    /// Find promotions that overlap on at least one SKU AND whose schedule
    /// windows could co-execute. Used by the schedule-time non-stacking
    /// overlap warning (FR-016).
    /// </summary>
    private static async Task<List<Guid>> FindOverlappingPromotionsAsync(
        PricingDbContext db, Promotion candidate, CancellationToken ct)
    {
        if (candidate.AppliesToProductIds is null or { Length: 0 })
        {
            return new List<Guid>();
        }
        // Only consider scheduled or active rules in the same market(s); ignore
        // self. Window-overlap check: [candidate.From, candidate.To) intersects
        // [other.From, other.To). Use coarse "any overlap on product ids" then
        // refine by window so we don't full-scan the table.
        var skuSet = candidate.AppliesToProductIds.ToHashSet();
        var sameMarket = await db.Promotions.AsNoTracking()
            .Where(p => p.Id != candidate.Id)
            .Where(p => p.State == LifecycleState.Scheduled || p.State == LifecycleState.Active)
            .Where(p => p.AppliesToProductIds != null)
            .Where(p => p.MarketCodes.Any(m => candidate.MarketCodes.Contains(m)))
            .Select(p => new { p.Id, p.AppliesToProductIds, p.StartsAt, p.EndsAt })
            .ToListAsync(ct);

        var result = new List<Guid>();
        foreach (var other in sameMarket)
        {
            if (other.AppliesToProductIds is null) continue;
            if (!other.AppliesToProductIds.Any(skuSet.Contains)) continue;
            // Window overlap: NOT (other.End <= candidate.Start OR other.Start >= candidate.End).
            // Null endpoints behave as open-ended on that side.
            var disjoint =
                (other.EndsAt is { } oe && candidate.StartsAt is { } cs && oe <= cs) ||
                (other.StartsAt is { } os && candidate.EndsAt is { } ce && os >= ce);
            if (disjoint) continue;
            result.Add(other.Id);
        }
        return result;
    }

    private static async Task<CommercialThresholdPolicy?> ResolveThresholdAsync(
        PricingDbContext db, string[] marketCodes, CancellationToken ct)
    {
        if (marketCodes.Length == 0) return null;
        var upper = marketCodes.Select(NormaliseMarketForThreshold).Where(m => m is not null).ToArray();
        if (upper.Length == 0) return null;

        var rows = await db.CommercialThresholds.AsNoTracking()
            .Where(t => upper.Contains(t.MarketCode))
            .ToListAsync(ct);
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

    private static string? NormaliseMarketForThreshold(string m) => m?.Trim().ToUpperInvariant() switch
    {
        "SA" or "KSA" => "SA",
        "EG" => "EG",
        _ => null,
    };

    /// <summary>
    /// Map the persisted Promotion to the high-impact gate candidate shape.
    /// PercentOff is whole percent (already in the threshold's unit); no scale
    /// conversion needed (unlike Coupon, which stores bps).
    /// </summary>
    private static HighImpactCandidate ToHighImpactCandidate(Promotion p) => new(
        PercentOff: p.Kind == "percent_off" ? p.PercentOff : null,
        AmountOffMinor: p.Kind == "amount_off" ? p.AmountOffMinor : null,
        CapMinor: null,                   // promotions don't carry a global cap today
        PerCustomerLimit: null,
        OverallLimit: null,
        ValidFrom: p.StartsAt,
        ValidTo: p.EndsAt)
    {
        // Promotions don't author usage limits — Criterion 3 must short-circuit
        // to a no-trip (FR-025 applies the "both unset" rule to coupons only).
        UsageLimitsApplicable = false,
    };

    private static object SnapshotForAudit(Promotion p) => new
    {
        p.Id,
        p.Kind,
        p.PercentOff,
        p.AmountOffMinor,
        p.RewardSku,
        p.BundleSku,
        applies_to_product_ids = p.AppliesToProductIds,
        applies_to_category_ids = p.AppliesToCategoryIds,
        markets = p.MarketCodes,
        p.Priority,
        p.StartsAt,
        p.EndsAt,
        p.StacksWithCoupons,
        p.StacksWithOtherPromotions,
        p.BannerEligible,
        label = new { ar = p.LabelAr, en = p.LabelEn },
        description = p.DescriptionAr is null && p.DescriptionEn is null ? null : new { ar = p.DescriptionAr, en = p.DescriptionEn },
        state = p.State.ToString().ToLowerInvariant(),
    };

    private static CommercialPromotionResponse ToResponse(Promotion p, IReadOnlyList<CommercialPromotionAuditSummaryRow>? auditSummary) =>
        new(
            Id: p.Id,
            Kind: p.Kind,
            State: p.State.ToString().ToLowerInvariant(),
            Markets: p.MarketCodes,
            PercentOff: p.Kind == "percent_off" ? p.PercentOff : null,
            AmountOffMinor: p.Kind == "amount_off" ? p.AmountOffMinor : null,
            RewardSku: p.Kind == "bogo" ? p.RewardSku : null,
            BundleSku: p.Kind == "bundle" ? p.BundleSku : null,
            AppliesToProductIds: p.AppliesToProductIds,
            AppliesToCategoryIds: p.AppliesToCategoryIds,
            Priority: p.Priority,
            ValidFrom: p.StartsAt,
            ValidTo: p.EndsAt,
            StacksWithCoupons: p.StacksWithCoupons,
            StacksWithOtherPromotions: p.StacksWithOtherPromotions,
            BannerEligible: p.BannerEligible,
            Label: new CommercialPromotionBilingual(p.LabelAr, p.LabelEn),
            Description: p.DescriptionAr is null && p.DescriptionEn is null
                ? null
                : new CommercialPromotionBilingual(p.DescriptionAr ?? string.Empty, p.DescriptionEn ?? string.Empty),
            RowVersion: p.XminRowVersion,
            CreatedAtUtc: p.CreatedAt,
            StateChangedAtUtc: p.StateChangedAtUtc,
            AuthorActorId: p.AuthorActorId,
            AuditSummary: auditSummary);
}
