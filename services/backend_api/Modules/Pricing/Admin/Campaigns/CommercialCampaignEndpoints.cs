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

namespace BackendApi.Modules.Pricing.Admin.Campaigns;

/// <summary>
/// Spec 007-b commercial Campaign endpoints (US4 / contract §5). A campaign
/// wraps a Promotion or Coupon with bilingual merchandising metadata and a
/// scheduled window; spec 024 CMS consumes the lookup endpoint to power the
/// banner-side picker (FR-020).
///
/// Single-file pattern matches the US1/US2/US3 cadence. Lifecycle uses the
/// shared <see cref="LifecycleStateMachine"/>; the high-impact gate does NOT
/// apply to campaigns (data-model §3.1) since campaigns do not themselves
/// affect engine pricing.
/// </summary>
public static class CommercialCampaignEndpoints
{
    private const string ActorRole = CommercialPermissions.Operator;
    private const int NameMaxLen = 300;
    private const int LandingQueryMaxLen = 1024;
    private const int NotesMaxLen = 4000;
    private const int ReasonNoteMinLen = 10;
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const int LookupPageCap = 200;

    public static IEndpointRouteBuilder MapCommercialCampaignEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/campaigns");
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };

        group.MapGet("", ListAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        group.MapGet("/lookups", LookupsAsync)
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
        group.MapDelete("/{id:guid}", DeleteForbiddenAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        return builder;
    }

    // -------------------- POST /campaigns --------------------

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateCampaignRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        TimeProvider time,
        CancellationToken ct)
    {
        var validationError = ValidateCreate(request);
        if (validationError is not null) return validationError(context);

        var normalisedMarkets = NormaliseMarkets(request.Markets);
        if (normalisedMarkets.Length == 0)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                "campaign.markets.empty_or_invalid",
                "At least one non-blank market is required.");
        }

        var linkValidation = await ValidateCampaignLinkAsync(request.CampaignLink, db, context, ct);
        if (linkValidation is not null) return linkValidation;

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var nowUtc = time.GetUtcNow();
        var campaignId = Guid.NewGuid();

        var entity = new Campaign
        {
            Id = campaignId,
            NameAr = request.Name!.Ar.Trim(),
            NameEn = request.Name!.En.Trim(),
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            Markets = normalisedMarkets,
            LandingQuery = string.IsNullOrWhiteSpace(request.LandingQuery) ? null : request.LandingQuery.Trim(),
            NotesInternal = string.IsNullOrWhiteSpace(request.NotesInternal) ? null : request.NotesInternal.Trim(),
            State = LifecycleState.Draft,
            StateChangedAtUtc = nowUtc,
            StateChangedByActorId = actorId,
            LinkBroken = false,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        db.Campaigns.Add(entity);

        CampaignLink? link = null;
        if (request.CampaignLink is not null)
        {
            link = new CampaignLink
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                // CodeRabbit PR #81 round 1 Major: persist the normalised kind
                // so downstream equality checks in the watcher + lookups match
                // (ValidateCampaignLinkAsync already trims/lowercases for
                // validation — without this, " Promotion " would pass and then
                // never match "promotion" in CampaignLinkBrokenWatcher).
                Kind = NormaliseLinkKind(request.CampaignLink.Kind),
                TargetId = request.CampaignLink.TargetId,
                LinkBrokenAtUtc = null,
                CreatedAt = nowUtc,
            };
            db.CampaignLinks.Add(link);
        }

        var publishAudit = audit.StageLocal(
            "campaign", campaignId, "campaign.created",
            actorId, ActorRole,
            before: null,
            after: SnapshotForAudit(entity, link),
            diff: null,
            reasonNote: null,
            correlationId: null,
            nowUtc);

        await db.SaveChangesAsync(ct);
        await publishAudit(ct);

        return Results.Created(
            $"/v1/admin/commercial/campaigns/{entity.Id:N}",
            ToResponse(entity, link, auditSummary: null));
    }

    // -------------------- PATCH /campaigns/{id} --------------------

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateCampaignRequest request,
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

        var entity = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.row.not_found", "Campaign not found.");
        }

        if (ifMatch is not null && ifMatch.Value != entity.XminRowVersion)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Campaign was changed by another actor; reload before retry.");
        }

        if (entity.State == LifecycleState.Expired)
        {
            return AdminCommercialResponseFactory.Problem(context, 403,
                CommercialReasonCode.CommercialRowExpiredTerminal,
                "Expired campaigns are read-only.");
        }

        // Mirror of Coupon/Promotion FR-004: when active, the schedule window
        // is locked. Name/notes/landing_query are non-pricing-equivalent here
        // (they don't affect engine resolution) and are always editable.
        if (entity.State == LifecycleState.Active &&
            (request.ValidFrom is not null || request.ValidTo is not null))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                "campaign.locked.active_window",
                "Campaign window is locked while the campaign is active. Deactivate first.");
        }

        if (request.Name is not null &&
            (string.IsNullOrWhiteSpace(request.Name.Ar) || string.IsNullOrWhiteSpace(request.Name.En)))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CampaignNameRequiredBilingual,
                "Both AR and EN names are required.");
        }
        if (request.Name is not null &&
            (request.Name.Ar.Length > NameMaxLen || request.Name.En.Length > NameMaxLen))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialTextTooLong,
                $"Name exceeds {NameMaxLen} characters.");
        }
        if (request.LandingQuery is not null && request.LandingQuery.Length > LandingQueryMaxLen)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialTextTooLong,
                $"landing_query exceeds {LandingQueryMaxLen} characters.");
        }
        if (request.NotesInternal is not null && request.NotesInternal.Length > NotesMaxLen)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialTextTooLong,
                $"notes_internal exceeds {NotesMaxLen} characters.");
        }

        string[]? normalisedMarketsForUpdate = null;
        if (request.Markets is not null)
        {
            normalisedMarketsForUpdate = NormaliseMarkets(request.Markets);
            if (normalisedMarketsForUpdate.Length == 0)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    "campaign.markets.empty_or_invalid",
                    "At least one non-blank market is required.");
            }
        }

        var effectiveValidFrom = request.ValidFrom ?? entity.ValidFrom;
        var effectiveValidTo = request.ValidTo ?? entity.ValidTo;
        if (effectiveValidTo <= effectiveValidFrom)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                "campaign.schedule.invalid_window",
                "valid_to must be strictly after valid_from.");
        }

        // Optional link replacement.
        CampaignLink? existingLink = await db.CampaignLinks
            .FirstOrDefaultAsync(l => l.CampaignId == id && l.LinkBrokenAtUtc == null, ct);
        if (request.CampaignLink is not null)
        {
            var linkValidation = await ValidateCampaignLinkAsync(request.CampaignLink, db, context, ct);
            if (linkValidation is not null) return linkValidation;
        }

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var nowUtc = time.GetUtcNow();
        var before = SnapshotForAudit(entity, existingLink);

        if (request.Name is not null)
        {
            entity.NameAr = request.Name.Ar.Trim();
            entity.NameEn = request.Name.En.Trim();
        }
        if (request.ValidFrom is not null) entity.ValidFrom = request.ValidFrom.Value;
        if (request.ValidTo is not null) entity.ValidTo = request.ValidTo.Value;
        if (normalisedMarketsForUpdate is not null) entity.Markets = normalisedMarketsForUpdate;
        if (request.LandingQuery is not null)
        {
            entity.LandingQuery = string.IsNullOrWhiteSpace(request.LandingQuery) ? null : request.LandingQuery.Trim();
        }
        if (request.NotesInternal is not null)
        {
            entity.NotesInternal = string.IsNullOrWhiteSpace(request.NotesInternal) ? null : request.NotesInternal.Trim();
        }
        entity.UpdatedAt = nowUtc;

        // Link replacement: append-only — close the existing one (mark broken)
        // and add a new one. This preserves audit trail.
        if (request.CampaignLink is not null)
        {
            if (existingLink is not null)
            {
                existingLink.LinkBrokenAtUtc = nowUtc;
            }
            var newLink = new CampaignLink
            {
                Id = Guid.NewGuid(),
                CampaignId = entity.Id,
                // CodeRabbit PR #81 round 1 Major: persist normalised kind
                // (see CreateAsync for the rationale).
                Kind = NormaliseLinkKind(request.CampaignLink.Kind),
                TargetId = request.CampaignLink.TargetId,
                LinkBrokenAtUtc = null,
                CreatedAt = nowUtc,
            };
            db.CampaignLinks.Add(newLink);
            existingLink = newLink;
            entity.LinkBroken = false;
        }

        var after = SnapshotForAudit(entity, existingLink);
        var publishAudit = audit.StageLocal(
            "campaign", entity.Id, "campaign.updated",
            actorId, ActorRole,
            before, after, new { before, after },
            reasonNote: null, correlationId: null, nowUtc);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Campaign was changed by another actor.");
        }
        await publishAudit(ct);

        return Results.Ok(ToResponse(entity, existingLink, null));
    }

    // -------------------- POST /campaigns/{id}/schedule --------------------

    private static async Task<IResult> ScheduleAsync(
        Guid id,
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

        var entity = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.row.not_found", "Campaign not found.");
        }

        if (ifMatch is not null && ifMatch.Value != entity.XminRowVersion)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Campaign was changed by another actor.");
        }

        if (entity.State != LifecycleState.Draft)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowInvalidTransition,
                $"Cannot schedule from state '{entity.State}'.");
        }

        if (entity.ValidTo <= entity.ValidFrom)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                "campaign.schedule.invalid_window",
                "valid_to must be strictly after valid_from.");
        }

        // CodeRabbit PR #81 round 1 Major: revalidate the current link target
        // before transitioning. If the linked coupon/promotion became expired
        // or its display flag changed between authoring and scheduling, the
        // campaign would otherwise leave Draft pointing at an invalid target.
        var currentLink = await db.CampaignLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.CampaignId == entity.Id && l.LinkBrokenAtUtc == null, ct);
        if (currentLink is not null && currentLink.Kind != "landing_only" && currentLink.TargetId is not null)
        {
            var linkRevalidation = await ValidateCampaignLinkAsync(
                new CampaignLinkRequest(currentLink.Kind, currentLink.TargetId),
                db, context, ct);
            if (linkRevalidation is not null) return linkRevalidation;
        }

        var nowUtc = time.GetUtcNow();
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
        entity.UpdatedAt = nowUtc;
        var after = new { state = entity.State.ToString().ToLowerInvariant() };

        var publishAudit = audit.StageLocal(
            "campaign", entity.Id, "campaign.lifecycle_transitioned",
            actorId, ActorRole,
            before, after,
            diff: new { state_change = new { from = before.state, to = after.state } },
            reasonNote: null, correlationId: null, nowUtc);

        await db.SaveChangesAsync(ct);
        await publishAudit(ct);

        var link = await db.CampaignLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.CampaignId == id && l.LinkBrokenAtUtc == null, ct);

        return Results.Ok(ToResponse(entity, link, null));
    }

    // -------------------- POST /campaigns/{id}/deactivate --------------------

    private static async Task<IResult> DeactivateAsync(
        Guid id,
        [FromBody] DeactivateCampaignRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
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

        var entity = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.row.not_found", "Campaign not found.");
        }

        if (ifMatch is not null && ifMatch.Value != entity.XminRowVersion)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Campaign was changed by another actor.");
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
        entity.UpdatedAt = nowUtc;
        var after = new { state = entity.State.ToString().ToLowerInvariant() };

        var publishAudit = audit.StageLocal(
            "campaign", entity.Id, "campaign.lifecycle_transitioned",
            actorId, ActorRole,
            before, after,
            diff: new { state_change = new { from = before.state, to = after.state } },
            reasonNote: note, correlationId: null, nowUtc);

        await db.SaveChangesAsync(ct);
        await publishAudit(ct);

        var link = await db.CampaignLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.CampaignId == id && l.LinkBrokenAtUtc == null, ct);

        return Results.Ok(ToResponse(entity, link, null));
    }

    // -------------------- GET /campaigns --------------------

    private static async Task<IResult> ListAsync(
        [FromQuery] string? state,
        [FromQuery] string? markets,
        [FromQuery] string? q,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        PricingDbContext db,
        CancellationToken ct)
    {
        var pageSize = limit is null or < 1 ? DefaultPageSize : Math.Min(limit.Value, MaxPageSize);

        IQueryable<Campaign> query = db.Campaigns.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(state) &&
            Enum.TryParse<LifecycleState>(state, ignoreCase: true, out var stateEnum))
        {
            query = query.Where(c => c.State == stateEnum);
        }
        if (!string.IsNullOrWhiteSpace(markets))
        {
            var marketArr = markets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(m => m.ToLowerInvariant()).ToArray();
            query = query.Where(c => c.Markets.Any(mc => marketArr.Contains(mc)));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pat = $"%{q.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.NameAr, pat) ||
                EF.Functions.ILike(c.NameEn, pat));
        }
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

        // Bulk-load active links once for the page.
        var ids = rows.Select(c => c.Id).ToList();
        var links = await db.CampaignLinks.AsNoTracking()
            .Where(l => ids.Contains(l.CampaignId) && l.LinkBrokenAtUtc == null)
            .ToListAsync(ct);
        var linkByCampaign = links.ToDictionary(l => l.CampaignId);

        return Results.Ok(new CommercialCampaignListResponse(
            rows.Select(c =>
                ToResponse(c, linkByCampaign.GetValueOrDefault(c.Id), null)).ToArray(),
            nextCursor));
    }

    // -------------------- GET /campaigns/{id} --------------------

    private static async Task<IResult> GetAsync(
        Guid id,
        PricingDbContext db,
        CancellationToken ct)
    {
        var entity = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return Results.NotFound();
        }
        var link = await db.CampaignLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.CampaignId == id && l.LinkBrokenAtUtc == null, ct);

        var summary = await db.CommercialAuditEvents.AsNoTracking()
            .Where(e => e.TargetEntityKind == "campaign" && e.TargetEntityId == id)
            .OrderByDescending(e => e.RecordedAtUtc)
            .Take(10)
            .Select(e => new CampaignAuditSummaryRow(e.Kind, e.ActorId, e.ActorRole, e.RecordedAtUtc, e.ReasonNote))
            .ToListAsync(ct);

        return Results.Ok(ToResponse(entity, link, summary));
    }

    // -------------------- GET /campaigns/lookups --------------------

    /// <summary>
    /// Banner-link picker consumed by spec 024 CMS (FR-020). Returns campaigns
    /// that overlap the given market AND are in <c>scheduled</c> or <c>active</c>.
    /// </summary>
    private static async Task<IResult> LookupsAsync(
        [FromQuery(Name = "market_code")] string? marketCode,
        [FromQuery] string? q,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        HttpContext context,
        PricingDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(marketCode))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                "campaign.markets.empty_or_invalid",
                "market_code is required.");
        }

        var marketLower = marketCode.Trim().ToLowerInvariant();
        var pageSize = limit is null or < 1 ? DefaultPageSize : Math.Min(limit.Value, LookupPageCap);

        // CodeRabbit PR #81 round 1 Major: exclude campaigns whose link has
        // been auto-marked broken (FR-019 — CampaignLinkBrokenWatcher sets
        // Campaign.LinkBroken=true when the linked rule deactivates/expires).
        // CMS' banner picker must not surface those.
        IQueryable<Campaign> query = db.Campaigns.AsNoTracking()
            .Where(c => !c.LinkBroken &&
                        (c.State == LifecycleState.Scheduled || c.State == LifecycleState.Active) &&
                        c.Markets.Any(m => m == marketLower));

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pat = $"%{q.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.NameAr, pat) ||
                EF.Functions.ILike(c.NameEn, pat));
        }

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

        var ids = rows.Select(c => c.Id).ToList();
        var links = await db.CampaignLinks.AsNoTracking()
            .Where(l => ids.Contains(l.CampaignId) && l.LinkBrokenAtUtc == null)
            .ToListAsync(ct);
        var linkByCampaign = links.ToDictionary(l => l.CampaignId);

        return Results.Ok(new CampaignLookupResponse(
            rows.Select(c =>
            {
                var link = linkByCampaign.GetValueOrDefault(c.Id);
                return new CampaignLookupRow(
                    c.Id,
                    new CampaignBilingual(c.NameAr, c.NameEn),
                    c.State.ToString().ToLowerInvariant(),
                    c.Markets,
                    c.ValidFrom,
                    c.ValidTo,
                    link?.Kind,
                    link?.TargetId);
            }).ToArray(),
            nextCursor));
    }

    // -------------------- DELETE /campaigns/{id} --------------------

    private static IResult DeleteForbiddenAsync(Guid id, HttpContext context) =>
        AdminCommercialResponseFactory.Problem(context, 405,
            CommercialReasonCode.CommercialRowDeleteForbidden,
            "Hard-delete is forbidden; deactivate instead.");

    // -------------------- helpers --------------------

    private static Func<HttpContext, IResult>? ValidateCreate(CreateCampaignRequest? request)
    {
        if (request is null)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CampaignNameRequiredBilingual, "Request body is required.");
        }
        if (request.Name is null ||
            string.IsNullOrWhiteSpace(request.Name.Ar) ||
            string.IsNullOrWhiteSpace(request.Name.En))
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CampaignNameRequiredBilingual,
                "Both AR and EN names are required.");
        }
        if (request.Name.Ar.Length > NameMaxLen || request.Name.En.Length > NameMaxLen)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CommercialTextTooLong,
                $"Name exceeds {NameMaxLen} characters.");
        }
        if (request.LandingQuery is not null && request.LandingQuery.Length > LandingQueryMaxLen)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CommercialTextTooLong,
                $"landing_query exceeds {LandingQueryMaxLen} characters.");
        }
        if (request.NotesInternal is not null && request.NotesInternal.Length > NotesMaxLen)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                CommercialReasonCode.CommercialTextTooLong,
                $"notes_internal exceeds {NotesMaxLen} characters.");
        }
        if (request.Markets is null || request.Markets.Length == 0)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                "campaign.markets.empty_or_invalid",
                "At least one market is required.");
        }
        if (request.ValidTo <= request.ValidFrom)
        {
            return ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                "campaign.schedule.invalid_window",
                "valid_to must be strictly after valid_from.");
        }
        return null;
    }

    /// <summary>
    /// FR-019 / contract §5.1 link validations. A landing-only link has
    /// no target. A promotion link must point at a non-expired Promotion.
    /// A coupon link must point at a non-expired Coupon with
    /// <c>display_in_banners=true</c>.
    /// </summary>
    private static async Task<IResult?> ValidateCampaignLinkAsync(
        CampaignLinkRequest? link,
        PricingDbContext db,
        HttpContext context,
        CancellationToken ct)
    {
        if (link is null) return null;

        var kind = link.Kind?.Trim().ToLowerInvariant();
        if (kind != "promotion" && kind != "coupon" && kind != "landing_only")
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                "campaign.link.invalid_kind",
                "kind must be one of: promotion, coupon, landing_only.");
        }

        if (kind == "landing_only")
        {
            if (link.TargetId is not null)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    "campaign.link.invalid_kind",
                    "landing_only links must not carry a target_id.");
            }
            return null;
        }

        if (link.TargetId is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CampaignLinkTargetNotFound,
                "target_id is required for promotion/coupon links.");
        }

        if (kind == "promotion")
        {
            var promo = await db.Promotions.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == link.TargetId, ct);
            if (promo is null)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CampaignLinkTargetNotFound,
                    "Linked promotion not found.");
            }
            if (promo.State == LifecycleState.Expired)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CampaignLinkTargetExpired,
                    "Linked promotion is expired.");
            }
        }
        else // coupon
        {
            var coupon = await db.Coupons.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == link.TargetId, ct);
            if (coupon is null)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CampaignLinkTargetNotFound,
                    "Linked coupon not found.");
            }
            if (coupon.State == LifecycleState.Expired)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CampaignLinkTargetExpired,
                    "Linked coupon is expired.");
            }
            if (!coupon.DisplayInBanners)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CampaignLinkCouponNotDisplayable,
                    "Linked coupon has display_in_banners=false; toggle it on the coupon first.");
            }
        }

        return null;
    }

    /// <summary>
    /// Mirrors the trim+lowercase normalisation done inside
    /// <see cref="ValidateCampaignLinkAsync"/>. Used by Create/Update so the
    /// persisted Kind always matches the validator's view of the world and the
    /// equality checks performed by <c>CampaignLinkBrokenWatcher</c> +
    /// banner-link lookups (CodeRabbit PR #81 round 1 Major).
    /// </summary>
    private static string NormaliseLinkKind(string raw) =>
        (raw ?? string.Empty).Trim().ToLowerInvariant();

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

    private static object SnapshotForAudit(Campaign c, CampaignLink? link) => new
    {
        c.Id,
        name = new { ar = c.NameAr, en = c.NameEn },
        c.ValidFrom,
        c.ValidTo,
        markets = c.Markets,
        c.LandingQuery,
        c.NotesInternal,
        state = c.State.ToString().ToLowerInvariant(),
        c.LinkBroken,
        link = link is null ? null : new { link.Id, link.Kind, link.TargetId, link.LinkBrokenAtUtc },
    };

    private static CommercialCampaignResponse ToResponse(
        Campaign c,
        CampaignLink? link,
        IReadOnlyList<CampaignAuditSummaryRow>? auditSummary) =>
        new(
            Id: c.Id,
            State: c.State.ToString().ToLowerInvariant(),
            Name: new CampaignBilingual(c.NameAr, c.NameEn),
            ValidFrom: c.ValidFrom,
            ValidTo: c.ValidTo,
            Markets: c.Markets,
            LandingQuery: c.LandingQuery,
            NotesInternal: c.NotesInternal,
            LinkBroken: c.LinkBroken,
            CampaignLink: link is null
                ? null
                : new CampaignLinkResponse(link.Kind, link.TargetId, link.LinkBrokenAtUtc),
            RowVersion: c.XminRowVersion,
            CreatedAtUtc: c.CreatedAt,
            StateChangedAtUtc: c.StateChangedAtUtc,
            AuditSummary: auditSummary);
}
