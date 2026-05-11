using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.Pricing.Admin.BusinessPricing;

/// <summary>
/// Spec 007-b commercial business-pricing endpoints (US3). Implements contract §4 —
/// tier-row + company-override upsert, deactivate/reactivate, list, get, conditionally
/// forbidden delete, and bulk-import preview/commit.
/// </summary>
public static class CommercialBusinessPricingEndpoints
{
    private const string ActorRole = CommercialPermissions.B2BAuthoring;
    private const int ReasonNoteMinLen = 10;

    public static IEndpointRouteBuilder MapCommercialBusinessPricingEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/business-pricing");
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };

        group.MapPut("/tier-rows", UpsertTierRowAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.B2BAuthoring);
        group.MapPut("/company-overrides", UpsertCompanyOverrideAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.B2BAuthoring);
        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.B2BAuthoring);
        group.MapPost("/{id:guid}/reactivate", ReactivateAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.B2BAuthoring);
        group.MapGet("", ListAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.B2BAuthoring);
        group.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.B2BAuthoring);
        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.B2BAuthoring);

        // Bulk-import surface (contract §4.3 / §4.4) — preview / commit.
        group.MapPost("/bulk-import/preview", CommercialBulkImport.PreviewAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.B2BAuthoring)
            .DisableAntiforgery();
        group.MapPost("/bulk-import/commit", CommercialBulkImport.CommitAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.B2BAuthoring);

        return builder;
    }

    // -------------------- PUT /business-pricing/tier-rows --------------------

    private static async Task<IResult> UpsertTierRowAsync(
        [FromBody] UpsertTierRowRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        TimeProvider time,
        CancellationToken ct)
    {
        if (request is null) return BadRequest(context, "Request body is required.");
        if (request.TierId == Guid.Empty) return BadRequest(context, "tier_id is required.");
        if (request.ProductId == Guid.Empty) return BadRequest(context, "product_id is required.");
        if (string.IsNullOrWhiteSpace(request.MarketCode)) return BadRequest(context, "market_code is required.");
        if (request.NetMinor < 0) return BadRequest(context, "net_minor must be >= 0.");

        var market = request.MarketCode.Trim().ToLowerInvariant();

        var tierExists = await db.B2BTiers.AnyAsync(t => t.Id == request.TierId, ct);
        if (!tierExists)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "business_pricing.tier.not_found", "Unknown tier_id.");
        }

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var nowUtc = time.GetUtcNow();

        // Find existing ACTIVE tier-only row (matches the unique partial index).
        var existing = await db.ProductTierPrices
            .SingleOrDefaultAsync(p =>
                p.TierId == request.TierId
                && p.CompanyId == null
                && p.MarketCode == market
                && p.ProductId == request.ProductId
                && p.State == BusinessPricingState.Active, ct);

        ProductTierPrice entity;
        object? before;
        if (existing is null)
        {
            entity = new ProductTierPrice
            {
                Id = Guid.NewGuid(),
                ProductId = request.ProductId,
                TierId = request.TierId,
                CompanyId = null,
                MarketCode = market,
                NetMinor = request.NetMinor,
                State = BusinessPricingState.Active,
                StateChangedAtUtc = nowUtc,
                StateChangedByActorId = actorId,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
            };
            db.ProductTierPrices.Add(entity);
            before = null;
        }
        else
        {
            before = SnapshotForAudit(existing);
            existing.NetMinor = request.NetMinor;
            existing.UpdatedAt = nowUtc;
            entity = existing;
        }

        var publishAudit = audit.StageLocal(
            "business_pricing", entity.Id, "business_pricing.row_changed",
            actorId, ActorRole,
            before, SnapshotForAudit(entity),
            diff: null,
            reasonNote: null,
            correlationId: null,
            nowUtc);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.BusinessPricingRowConflict,
                "Tier row already exists for this (tier, product, market).");
        }
        catch (DbUpdateException ex) when (IsCheckViolation(ex))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.BusinessPricingRowXorViolation,
                "Row violates tier/company XOR constraint.");
        }
        await publishAudit(ct);

        return Results.Ok(ToResponse(entity));
    }

    // -------------------- PUT /business-pricing/company-overrides --------------------

    private static async Task<IResult> UpsertCompanyOverrideAsync(
        [FromBody] UpsertCompanyOverrideRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        TimeProvider time,
        CancellationToken ct)
    {
        if (request is null) return BadRequest(context, "Request body is required.");
        if (request.CompanyId == Guid.Empty) return BadRequest(context, "company_id is required.");
        if (request.ProductId == Guid.Empty) return BadRequest(context, "product_id is required.");
        if (string.IsNullOrWhiteSpace(request.MarketCode)) return BadRequest(context, "market_code is required.");
        if (request.NetMinor < 0) return BadRequest(context, "net_minor must be >= 0.");

        var market = request.MarketCode.Trim().ToLowerInvariant();
        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var nowUtc = time.GetUtcNow();

        // If a copy-source tier_id is provided, verify it exists and matches market.
        if (request.CopiedFromTierId is { } src && src != Guid.Empty)
        {
            var srcExists = await db.B2BTiers.AnyAsync(t => t.Id == src, ct);
            if (!srcExists)
            {
                return AdminCommercialResponseFactory.Problem(context, 404,
                    "business_pricing.tier.not_found", "Unknown copied_from_tier_id.");
            }
        }

        var existing = await db.ProductTierPrices
            .SingleOrDefaultAsync(p =>
                p.CompanyId == request.CompanyId
                && p.MarketCode == market
                && p.ProductId == request.ProductId
                && p.State == BusinessPricingState.Active, ct);

        ProductTierPrice entity;
        object? before;
        if (existing is null)
        {
            entity = new ProductTierPrice
            {
                Id = Guid.NewGuid(),
                ProductId = request.ProductId,
                // Override row: TierId is set ONLY when CopiedFromTierId is also set
                // (the "audit-pointed" shape from the chk_tier_xor_company constraint).
                // Standalone overrides have TierId NULL.
                TierId = request.CopiedFromTierId is { } id && id != Guid.Empty ? id : null,
                CompanyId = request.CompanyId,
                CopiedFromTierId = request.CopiedFromTierId is { } cid && cid != Guid.Empty ? cid : null,
                MarketCode = market,
                NetMinor = request.NetMinor,
                State = BusinessPricingState.Active,
                StateChangedAtUtc = nowUtc,
                StateChangedByActorId = actorId,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
            };
            db.ProductTierPrices.Add(entity);
            before = null;
        }
        else
        {
            before = SnapshotForAudit(existing);
            existing.NetMinor = request.NetMinor;
            existing.UpdatedAt = nowUtc;
            entity = existing;
        }

        var publishAudit = audit.StageLocal(
            "business_pricing", entity.Id, "business_pricing.row_changed",
            actorId, ActorRole,
            before, SnapshotForAudit(entity),
            diff: null, reasonNote: null, correlationId: null,
            nowUtc);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.BusinessPricingRowConflict,
                "Company override already exists for this (company, product, market).");
        }
        catch (DbUpdateException ex) when (IsCheckViolation(ex))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.BusinessPricingRowXorViolation,
                "Row violates tier/company XOR constraint.");
        }
        await publishAudit(ct);

        return Results.Ok(ToResponse(entity));
    }

    // -------------------- POST /business-pricing/{id}/deactivate --------------------

    private static async Task<IResult> DeactivateAsync(
        Guid id,
        [FromBody] DeactivateOrReactivateBusinessPricingRequest request,
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

        var entity = await db.ProductTierPrices.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.row.not_found", "Business pricing row not found.");
        }

        if (!BusinessPricingStateMachine.TryTransition(
                entity.State, BusinessPricingTrigger.Deactivate,
                out var newState, out var reason))
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
            "business_pricing", entity.Id, "business_pricing.row_changed",
            actorId, ActorRole,
            before, after,
            diff: new { state_change = new { from = before.state, to = after.state } },
            reasonNote: note, correlationId: null, nowUtc);

        await db.SaveChangesAsync(ct);
        await publishAudit(ct);

        return Results.Ok(ToResponse(entity));
    }

    // -------------------- POST /business-pricing/{id}/reactivate --------------------

    private static async Task<IResult> ReactivateAsync(
        Guid id,
        [FromBody] DeactivateOrReactivateBusinessPricingRequest request,
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

        var entity = await db.ProductTierPrices.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.row.not_found", "Business pricing row not found.");
        }

        if (!BusinessPricingStateMachine.TryTransition(
                entity.State, BusinessPricingTrigger.Reactivate,
                out var newState, out var reason))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                reason ?? CommercialReasonCode.CommercialRowInvalidTransition,
                "Reactivate transition rejected.");
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
            "business_pricing", entity.Id, "business_pricing.row_changed",
            actorId, ActorRole,
            before, after,
            diff: new { state_change = new { from = before.state, to = after.state } },
            reasonNote: note, correlationId: null, nowUtc);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.BusinessPricingRowConflict,
                "Re-activating this row would conflict with an existing active row.");
        }
        await publishAudit(ct);

        return Results.Ok(ToResponse(entity));
    }

    // -------------------- GET /business-pricing --------------------

    private static async Task<IResult> ListAsync(
        [FromQuery] Guid? tier_id,
        [FromQuery] Guid? company_id,
        [FromQuery] Guid? product_id,
        [FromQuery] string? market,
        [FromQuery] string? state,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        PricingDbContext db,
        CancellationToken ct)
    {
        var pageSize = limit is null or < 1 ? 50 : Math.Min(limit.Value, 200);

        IQueryable<ProductTierPrice> query = db.ProductTierPrices.AsNoTracking();
        if (tier_id is { } t) query = query.Where(p => p.TierId == t);
        if (company_id is { } c) query = query.Where(p => p.CompanyId == c);
        if (product_id is { } p) query = query.Where(x => x.ProductId == p);
        if (!string.IsNullOrWhiteSpace(market))
        {
            var m = market.Trim().ToLowerInvariant();
            query = query.Where(x => x.MarketCode == m);
        }
        if (!string.IsNullOrWhiteSpace(state)
            && Enum.TryParse<BusinessPricingState>(state, ignoreCase: true, out var stateEnum))
        {
            query = query.Where(x => x.State == stateEnum);
        }

        if (!string.IsNullOrWhiteSpace(cursor) && TryParseCursor(cursor, out var cursorAt, out var cursorId))
        {
            query = query.Where(x =>
                x.CreatedAt < cursorAt ||
                (x.CreatedAt == cursorAt && x.Id.CompareTo(cursorId) < 0));
        }

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > pageSize)
        {
            var last = rows[pageSize - 1];
            nextCursor = $"{last.CreatedAt.UtcTicks}:{last.Id:N}";
            rows = rows.Take(pageSize).ToList();
        }

        return Results.Ok(new BusinessPricingListResponse(
            rows.Select(ToResponse).ToArray(),
            nextCursor));
    }

    // -------------------- GET /business-pricing/{id} --------------------

    private static async Task<IResult> GetAsync(
        Guid id,
        PricingDbContext db,
        CancellationToken ct)
    {
        var entity = await db.ProductTierPrices.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return Results.NotFound();
        return Results.Ok(ToResponse(entity));
    }

    // -------------------- DELETE /business-pricing/{id} --------------------

    /// <summary>
    /// Contract §4.7 — DELETE is conditionally forbidden. If the row is
    /// referenced by any historical <c>price_explanations</c>, return 405
    /// (FR-005a). Otherwise allow the soft delete to proceed via state
    /// machine (Deactivated).
    /// </summary>
    private static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext context,
        PricingDbContext db,
        CancellationToken ct)
    {
        // CodeRabbit PR #80 round 1 Critical: must NOT use AsNoTracking() — the
        // entity is passed to db.Remove() below, which requires the change
        // tracker to know about the row. AsNoTracking + Remove throws at
        // SaveChangesAsync.
        var entity = await db.ProductTierPrices.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return Results.NotFound();

        // FR-005a: hard-delete forbidden when the row has been observed by the
        // engine (any PriceExplanation references it). For simplicity we treat
        // any row that ever transitioned through Active as historically observed.
        // The richer per-explanation reference check lands with the Polish phase
        // (T148 commercial integrity scan).
        if (entity.State == BusinessPricingState.Active
            || entity.StateChangedAtUtc != default)
        {
            return AdminCommercialResponseFactory.Problem(context, 405,
                CommercialReasonCode.CommercialRowDeleteForbidden,
                "Hard-delete is forbidden; deactivate this row instead.");
        }

        db.ProductTierPrices.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // -------------------- helpers --------------------

    /// <summary>
    /// 400-validation helper. CodeRabbit PR #80 round 1 Minor: uses the
    /// dedicated <see cref="CommercialReasonCode.BusinessPricingValidationError"/>
    /// code instead of <c>BusinessPricingRowConflict</c> (which is reserved for
    /// duplicate / uniqueness violations).
    /// </summary>
    private static IResult BadRequest(HttpContext context, string detail) =>
        AdminCommercialResponseFactory.Problem(context, 400,
            CommercialReasonCode.BusinessPricingValidationError, detail);

    internal static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    internal static bool IsCheckViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation };

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

    internal static object SnapshotForAudit(ProductTierPrice e) => new
    {
        e.Id,
        e.ProductId,
        e.TierId,
        e.CompanyId,
        e.CopiedFromTierId,
        market = e.MarketCode,
        e.NetMinor,
        state = e.State.ToString().ToLowerInvariant(),
    };

    internal static BusinessPricingRowResponse ToResponse(ProductTierPrice e) => new(
        Id: e.Id,
        TierId: e.TierId,
        CompanyId: e.CompanyId,
        CopiedFromTierId: e.CopiedFromTierId,
        ProductId: e.ProductId,
        MarketCode: e.MarketCode,
        NetMinor: e.NetMinor,
        State: e.State.ToString().ToLowerInvariant(),
        CompanyLinkBroken: e.CompanyLinkBroken,
        CompanyLinkBrokenAtUtc: e.CompanyLinkBrokenAtUtc,
        VendorId: e.VendorId,
        RowVersion: e.XminRowVersion,
        CreatedAtUtc: e.CreatedAt,
        StateChangedAtUtc: e.StateChangedAtUtc);
}
