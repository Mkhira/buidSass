using System.Text.Json;
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

namespace BackendApi.Modules.Pricing.Admin.PreviewProfiles;

/// <summary>
/// Spec 007-b preview-profile endpoints (contract §6). Personal profiles are
/// scoped to their author + super_admin; shared profiles are visible to every
/// commercial.operator. Promotion to <c>shared</c> requires
/// <c>commercial.approver</c> per FR-031 (and is audited as
/// <c>preview_profile.visibility_changed</c>).
/// </summary>
public static class CommercialPreviewProfileEndpoints
{
    private const string ActorRole = CommercialPermissions.Operator;
    private const int NameMaxLen = 200;
    private const int CartMaxLines = 50;
    private const int ReasonNoteMinLen = 10;

    public static IEndpointRouteBuilder MapCommercialPreviewProfileEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/preview-profiles");
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };

        group.MapPut("", UpsertAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        group.MapGet("", ListAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        group.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        group.MapPost("/{id:guid}/promote-to-shared", PromoteToSharedAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        return builder;
    }

    // -------------------- PUT /preview-profiles (upsert) --------------------

    private static async Task<IResult> UpsertAsync(
        [FromBody] UpsertPreviewProfileRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        CommercialActorPermissions perms,
        TimeProvider time,
        CancellationToken ct)
    {
        if (request is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.PreviewProfileNameRequired, "Body required.");
        }
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > NameMaxLen)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.PreviewProfileNameRequired,
                $"name is required and must be ≤ {NameMaxLen} characters.");
        }
        if (request.CartLines is null || request.CartLines.Count == 0 || request.CartLines.Count > CartMaxLines)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.PreviewProfileCartTooLarge,
                $"cart_lines must contain 1..{CartMaxLines} entries.");
        }
        if (request.CartLines.Any(l => string.IsNullOrWhiteSpace(l.Sku) || l.Qty < 1))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.PreviewProfileCartTooLarge,
                "Every cart line requires non-empty sku and qty >= 1.");
        }
        var market = request.MarketCode?.Trim().ToUpperInvariant();
        if (market is not ("SA" or "EG"))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowInvalidTransition,
                "market_code must be 'SA' or 'EG'.");
        }
        var locale = request.Locale?.Trim().ToLowerInvariant();
        if (locale is not ("ar" or "en"))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowInvalidTransition,
                "locale must be 'ar' or 'en'.");
        }
        var accountKind = request.AccountKind?.Trim().ToLowerInvariant();
        if (accountKind is not ("consumer" or "b2b"))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowInvalidTransition,
                "account_kind must be 'consumer' or 'b2b'.");
        }
        var verification = request.VerificationState?.Trim().ToLowerInvariant() ?? "none";
        if (verification is not ("none" or "submitted" or "approved" or "rejected" or "expired"))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowInvalidTransition,
                "verification_state invalid.");
        }
        // FR-031 / contract §6.1: setting visibility=shared in upsert is rejected.
        // Promotion goes through §6.2.
        var visibility = request.Visibility?.Trim().ToLowerInvariant() ?? "personal";
        if (visibility == "shared")
        {
            return AdminCommercialResponseFactory.Problem(context, 403,
                CommercialReasonCode.PreviewProfilePromoteRequiresApprover,
                "Promote a personal profile via /promote-to-shared instead.");
        }
        if (visibility != "personal")
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowInvalidTransition,
                "visibility must be 'personal'.");
        }

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var nowUtc = time.GetUtcNow();
        var cartJson = JsonSerializer.Serialize(request.CartLines.Select(l =>
            new { sku = l.Sku.Trim(), qty = l.Qty, restricted = l.Restricted }));

        PreviewProfile entity;
        bool isInsert;
        if (request.Id is { } existingId)
        {
            entity = await db.PreviewProfiles.FirstOrDefaultAsync(p => p.Id == existingId, ct)
                ?? throw new InvalidOperationException();

            if (entity is null)
            {
                return AdminCommercialResponseFactory.Problem(context, 404,
                    "commercial.row.not_found", "Preview profile not found.");
            }
            if (entity.CreatedBy != actorId
                && !await perms.HasSuperAdminAsync(context, ct))
            {
                return AdminCommercialResponseFactory.Problem(context, 403,
                    CommercialReasonCode.PreviewProfileNotVisibleToActor,
                    "Cannot edit a profile owned by another actor.");
            }
            entity.Name = request.Name.Trim();
            entity.MarketCode = market;
            entity.Locale = locale;
            entity.AccountKind = accountKind;
            entity.TierId = request.TierId;
            entity.VerificationState = verification;
            entity.CartLinesJson = cartJson;
            entity.UpdatedAt = nowUtc;
            isInsert = false;
        }
        else
        {
            entity = new PreviewProfile
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                MarketCode = market,
                Locale = locale,
                AccountKind = accountKind,
                TierId = request.TierId,
                VerificationState = verification,
                CartLinesJson = cartJson,
                Visibility = "personal",
                CreatedBy = actorId,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
            };
            db.PreviewProfiles.Add(entity);
            isInsert = true;
        }

        await db.SaveChangesAsync(ct);
        return isInsert
            ? Results.Created($"/v1/admin/commercial/preview-profiles/{entity.Id:N}", ToResponse(entity))
            : Results.Ok(ToResponse(entity));
    }

    // -------------------- POST /{id}/promote-to-shared --------------------

    private static async Task<IResult> PromoteToSharedAsync(
        Guid id,
        [FromBody] PromoteToSharedRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        CommercialActorPermissions perms,
        TimeProvider time,
        CancellationToken ct)
    {
        if (!await perms.HasApproverOrSuperAdminAsync(context, ct))
        {
            return AdminCommercialResponseFactory.Problem(context, 403,
                CommercialReasonCode.PreviewProfilePromoteRequiresApprover,
                "Caller lacks commercial.approver.");
        }
        if (string.IsNullOrWhiteSpace(request?.ReasonNote) || request.ReasonNote.Trim().Length < ReasonNoteMinLen)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialApprovalNoteTooShort,
                $"reason_note must be ≥ {ReasonNoteMinLen} characters.");
        }

        var entity = await db.PreviewProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.row.not_found", "Preview profile not found.");
        }
        if (entity.Visibility == "shared")
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.PreviewProfilePromoteRequiresApprover,
                "Profile is already shared.");
        }

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var nowUtc = time.GetUtcNow();
        var before = new { visibility = entity.Visibility };
        entity.Visibility = "shared";
        entity.UpdatedAt = nowUtc;
        var after = new { visibility = entity.Visibility };

        await audit.AppendAsync(
            "preview_profile", entity.Id, "preview_profile.visibility_changed",
            actorId, "commercial.approver",
            before, after, diff: new { before, after },
            reasonNote: request.ReasonNote.Trim(), correlationId: null, nowUtc, ct);

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(entity));
    }

    // -------------------- GET /preview-profiles --------------------

    private static async Task<IResult> ListAsync(
        HttpContext context,
        PricingDbContext db,
        CommercialActorPermissions perms,
        CancellationToken ct)
    {
        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var isSuper = await perms.HasSuperAdminAsync(context, ct);

        var query = db.PreviewProfiles.AsNoTracking();
        if (!isSuper)
        {
            query = query.Where(p => p.Visibility == "shared" || p.CreatedBy == actorId);
        }

        var rows = await query
            .OrderByDescending(p => p.UpdatedAt)
            .Take(200)
            .ToListAsync(ct);

        return Results.Ok(new PreviewProfileListResponse(rows.Select(ToResponse).ToArray()));
    }

    // -------------------- GET /preview-profiles/{id} --------------------

    private static async Task<IResult> GetAsync(
        Guid id,
        HttpContext context,
        PricingDbContext db,
        CommercialActorPermissions perms,
        CancellationToken ct)
    {
        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var entity = await db.PreviewProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
        {
            return Results.NotFound();
        }
        if (entity.Visibility != "shared"
            && entity.CreatedBy != actorId
            && !await perms.HasSuperAdminAsync(context, ct))
        {
            return AdminCommercialResponseFactory.Problem(context, 403,
                CommercialReasonCode.PreviewProfileNotVisibleToActor,
                "Profile not visible to caller.");
        }
        return Results.Ok(ToResponse(entity));
    }

    // -------------------- DELETE /preview-profiles/{id} --------------------

    /// <summary>
    /// Hard-delete IS allowed for personal profiles owned by the caller, and
    /// for any profile by super_admin (data-model §11 / FR-005a exception).
    /// </summary>
    private static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext context,
        PricingDbContext db,
        CommercialActorPermissions perms,
        CancellationToken ct)
    {
        var entity = await db.PreviewProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return Results.NoContent();

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var isOwner = entity.CreatedBy == actorId && entity.Visibility == "personal";
        var isSuper = await perms.HasSuperAdminAsync(context, ct);
        if (!isOwner && !isSuper)
        {
            return AdminCommercialResponseFactory.Problem(context, 403,
                CommercialReasonCode.PreviewProfileNotVisibleToActor,
                "Cannot delete a profile owned by another actor (or shared profile).");
        }

        db.PreviewProfiles.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // -------------------- helpers --------------------

    private static PreviewProfileResponse ToResponse(PreviewProfile p)
    {
        IReadOnlyList<PreviewCartLine> lines;
        try
        {
            var raw = JsonSerializer.Deserialize<List<JsonElement>>(p.CartLinesJson) ?? new();
            lines = raw.Select(e => new PreviewCartLine(
                e.GetProperty("sku").GetString() ?? string.Empty,
                e.GetProperty("qty").GetInt32(),
                e.TryGetProperty("restricted", out var r) && r.GetBoolean())).ToArray();
        }
        catch (JsonException)
        {
            lines = Array.Empty<PreviewCartLine>();
        }

        return new PreviewProfileResponse(
            Id: p.Id,
            Name: p.Name,
            MarketCode: p.MarketCode,
            Locale: p.Locale,
            AccountKind: p.AccountKind,
            TierId: p.TierId,
            VerificationState: p.VerificationState,
            CartLines: lines,
            Visibility: p.Visibility,
            CreatedBy: p.CreatedBy,
            RowVersion: p.XminRowVersion,
            CreatedAt: p.CreatedAt,
            UpdatedAt: p.UpdatedAt);
    }
}
