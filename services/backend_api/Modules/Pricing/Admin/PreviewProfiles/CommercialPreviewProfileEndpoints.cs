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
        BackendApi.Modules.AuditLog.IAuditEventPublisher platformAudit,
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

        PreviewProfile? entity;
        bool isInsert;
        object? auditBefore = null;
        if (request.Id is { } existingId)
        {
            // Don't throw on a missing id — return a clean 404 so callers
            // distinguish "unknown profile" from "server bug" (CodeRabbit
            // PR #78 round 1 Major).
            entity = await db.PreviewProfiles.FirstOrDefaultAsync(p => p.Id == existingId, ct);
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
            auditBefore = SnapshotForAudit(entity);
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

        // CodeRabbit PR #78 round 1 Major: every admin-managed mutation must
        // produce an audit trail. The local pricing.commercial_audit_events
        // table's chk_cae_kind constraint only allows the single
        // 'preview_profile.visibility_changed' kind for this entity (per
        // data-model §5), so create/update/delete actions on preview profiles
        // route through the platform audit_log_entries channel directly. Once
        // a future migration relaxes chk_cae_kind for preview_profile.* kinds,
        // these rows can also stage to commercial_audit_events for the
        // audit-summary panel.
        var actorRole = await ResolveActorRoleAsync(context, perms, ct);
        var auditAfter = SnapshotForAudit(entity);

        await db.SaveChangesAsync(ct);
        await platformAudit.PublishAsync(new BackendApi.Modules.AuditLog.AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: isInsert ? "preview_profile.created" : "preview_profile.updated",
            EntityType: "PreviewProfile",
            EntityId: entity.Id,
            BeforeState: auditBefore,
            AfterState: auditAfter,
            Reason: null), ct);
        return isInsert
            ? Results.Created($"/v1/admin/commercial/preview-profiles/{entity.Id:N}", ToResponse(entity))
            : Results.Ok(ToResponse(entity));
    }

    /// <summary>
    /// Returns the most-specific role the caller holds for audit-row
    /// attribution (CodeRabbit PR #78 round 1 Major: don't hardcode
    /// "commercial.approver" when super_admin is also allowed).
    /// </summary>
    private static async Task<string> ResolveActorRoleAsync(
        HttpContext context, CommercialActorPermissions perms, CancellationToken ct)
    {
        if (await perms.HasSuperAdminAsync(context, ct)) return "super_admin";
        if (await perms.HasApproverOrSuperAdminAsync(context, ct)) return CommercialPermissions.Approver;
        return CommercialPermissions.Operator;
    }

    private static object SnapshotForAudit(PreviewProfile p) => new
    {
        p.Id,
        p.Name,
        p.MarketCode,
        p.Locale,
        p.AccountKind,
        p.TierId,
        p.VerificationState,
        p.Visibility,
        p.CreatedBy,
    };

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

        // CodeRabbit PR #78 round 1 Major: route accepts both
        // commercial.approver and super_admin — record the actual caller role
        // so the audit channel can attribute the action accurately.
        var actorRole = await ResolveActorRoleAsync(context, perms, ct);
        var publishAudit = audit.StageLocal(
            "preview_profile", entity.Id, "preview_profile.visibility_changed",
            actorId, actorRole,
            before, after, diff: new { before, after },
            reasonNote: request.ReasonNote.Trim(), correlationId: null, nowUtc);

        await db.SaveChangesAsync(ct);
        await publishAudit(ct);
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
        BackendApi.Modules.AuditLog.IAuditEventPublisher platformAudit,
        CommercialActorPermissions perms,
        TimeProvider time,
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

        // CodeRabbit PR #78 round 1 Major: hard-delete still produces an
        // audit row in the platform channel (the row goes away, but the
        // action is auditable). chk_cae_kind doesn't accept a deleted kind
        // for preview_profile yet, so route through the cross-cutting
        // audit_log_entries channel directly.
        var actorRole = await ResolveActorRoleAsync(context, perms, ct);
        var beforeSnapshot = SnapshotForAudit(entity);
        db.PreviewProfiles.Remove(entity);
        await db.SaveChangesAsync(ct);
        await platformAudit.PublishAsync(new BackendApi.Modules.AuditLog.AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: "preview_profile.deleted",
            EntityType: "PreviewProfile",
            EntityId: entity.Id,
            BeforeState: beforeSnapshot,
            AfterState: null,
            Reason: null), ct);
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
