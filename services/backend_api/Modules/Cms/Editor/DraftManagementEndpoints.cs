using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Authorization;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Editor;

public sealed record DismissStaleAlertRequest(string ReasonNote);
public sealed record ReassignOwnershipRequest(Guid TargetActorId);

public static class DraftManagementEndpoints
{
    public static IEndpointRouteBuilder MapDraftManagementEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapDelete("/{kind}/drafts/{id:guid}", DeleteDraftAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapPost("/drafts/{id:guid}/dismiss-stale-alert", DismissStaleAlertAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        builder.MapPost("/drafts/{id:guid}/reassign-ownership", ReassignOwnershipAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });
        return builder;
    }

    private static async Task<IResult> DeleteDraftAsync(
        string kind,
        Guid id,
        HttpContext context,
        [FromServices] CmsDbContext db,
        [FromServices] IAuditEventPublisher audit,
        CancellationToken ct)
    {
        if (!CmsResponseFactory.HasPermissionClaim(context, CmsPermissions.Editor) &&
            !CmsResponseFactory.HasPermissionClaim(context, "super_admin"))
        {
            return CmsResponseFactory.Problem(context, 403,
                CmsReasonCode.EditorRoleRequired, "cms.editor permission required.");
        }
        var actorId = CmsResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return CmsResponseFactory.Problem(context, 401,
                CmsReasonCode.EditorRoleRequired, "Authenticated actor required.");
        }
        var actorRole = CmsResponseFactory.ResolveActorRole(context);

        var (deleted, ownerId, currentState) = await TryDeleteDraftAsync(db, kind, id, ct);
        if (currentState is null)
        {
            return CmsResponseFactory.Problem(context, 404,
                CmsReasonCode.PreviewEntityNotFound, "Draft not found.");
        }
        if (currentState != "draft")
        {
            return CmsResponseFactory.Problem(context, 405,
                BuildKindReason(kind, "delete_forbidden"),
                $"Hard-delete forbidden — current state '{currentState}'.");
        }
        var isSuperAdmin = CmsResponseFactory.HasPermissionClaim(context, "super_admin");
        if (!isSuperAdmin && ownerId != actorId.Value)
        {
            return CmsResponseFactory.Problem(context, 403,
                CmsReasonCode.DraftDeleteNotOwner,
                "Only the draft owner or super_admin may delete this draft.");
        }
        if (!deleted)
        {
            await CommitDeleteAsync(db, kind, id, ct);
        }

        await audit.PublishAsync(new AuditEvent(
            ActorId: actorId.Value,
            ActorRole: actorRole,
            Action: "cms.draft.deleted",
            EntityType: $"cms.{kind}",
            EntityId: id,
            BeforeState: new { State = "draft" },
            AfterState: new { State = "deleted" },
            Reason: "editor_delete_unpublished_draft"), ct);

        return Results.NoContent();
    }

    private static async Task<(bool Deleted, Guid OwnerId, string? CurrentState)>
        TryDeleteDraftAsync(CmsDbContext db, string kind, Guid id, CancellationToken ct)
    {
        switch (kind)
        {
            case "banner-slots":
            case "banner_slot":
                var bs = await db.BannerSlots.FirstOrDefaultAsync(b => b.Id == id, ct);
                return (false, bs?.OwnerActorId ?? Guid.Empty, bs?.StateWire);
            case "featured-sections":
            case "featured_section":
                var fs = await db.FeaturedSections.FirstOrDefaultAsync(f => f.Id == id, ct);
                return (false, fs?.OwnerActorId ?? Guid.Empty, fs?.StateWire);
            case "faq-entries":
            case "faq_entry":
                var fq = await db.FaqEntries.FirstOrDefaultAsync(f => f.Id == id, ct);
                return (false, fq?.OwnerActorId ?? Guid.Empty, fq?.StateWire);
            case "blog-articles":
            case "blog_article":
                var ba = await db.BlogArticles.FirstOrDefaultAsync(b => b.Id == id, ct);
                return (false, ba?.OwnerActorId ?? Guid.Empty, ba?.StateWire);
            default:
                return (false, Guid.Empty, null);
        }
    }

    private static async Task CommitDeleteAsync(CmsDbContext db, string kind, Guid id, CancellationToken ct)
    {
        switch (kind)
        {
            case "banner-slots":
            case "banner_slot":
                await db.BannerSlots.Where(b => b.Id == id && b.StateWire == "draft").ExecuteDeleteAsync(ct);
                break;
            case "featured-sections":
            case "featured_section":
                await db.FeaturedSections.Where(f => f.Id == id && f.StateWire == "draft").ExecuteDeleteAsync(ct);
                break;
            case "faq-entries":
            case "faq_entry":
                await db.FaqEntries.Where(f => f.Id == id && f.StateWire == "draft").ExecuteDeleteAsync(ct);
                break;
            case "blog-articles":
            case "blog_article":
                await db.BlogArticles.Where(b => b.Id == id && b.StateWire == "draft").ExecuteDeleteAsync(ct);
                break;
        }
    }

    private static async Task<IResult> DismissStaleAlertAsync(
        Guid id,
        [FromBody] DismissStaleAlertRequest? body,
        HttpContext context,
        [FromServices] CmsDbContext db,
        [FromServices] IAuditEventPublisher audit,
        CancellationToken ct)
    {
        if (!CmsResponseFactory.HasPermissionClaim(context, CmsPermissions.Editor) &&
            !CmsResponseFactory.HasPermissionClaim(context, CmsPermissions.Publisher))
        {
            return CmsResponseFactory.Problem(context, 403,
                CmsReasonCode.EditorRoleRequired, "cms.editor or cms.publisher permission required.");
        }
        if (body is null || string.IsNullOrWhiteSpace(body.ReasonNote) || body.ReasonNote.Length < 10)
        {
            return CmsResponseFactory.Problem(context, 400,
                CmsReasonCode.ArchiveReasonNoteRequired,
                "reason_note must be ≥ 10 chars.");
        }
        var actorId = CmsResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return CmsResponseFactory.Problem(context, 401,
                CmsReasonCode.EditorRoleRequired, "Authenticated actor required.");
        }
        var nowUtc = DateTimeOffset.UtcNow;
        var found =
            await UpdateLastDismissedAsync(db, db.BannerSlots, id, nowUtc, ct) ||
            await UpdateLastDismissedAsync(db, db.FeaturedSections, id, nowUtc, ct) ||
            await UpdateLastDismissedAsync(db, db.FaqEntries, id, nowUtc, ct) ||
            await UpdateLastDismissedAsync(db, db.BlogArticles, id, nowUtc, ct) ||
            await UpdateLastDismissedAsync(db, db.LegalPageVersions, id, nowUtc, ct);
        if (!found)
        {
            return CmsResponseFactory.Problem(context, 404,
                CmsReasonCode.PreviewEntityNotFound, "Draft not found.");
        }
        await audit.PublishAsync(new AuditEvent(
            ActorId: actorId.Value,
            ActorRole: CmsResponseFactory.ResolveActorRole(context),
            Action: "cms.draft.stale_alert_dismissed",
            EntityType: "cms.draft",
            EntityId: id,
            BeforeState: null,
            AfterState: new { DismissedAtUtc = nowUtc, body.ReasonNote },
            Reason: null), ct);
        return Results.NoContent();
    }

    private static async Task<bool> UpdateLastDismissedAsync<T>(
        CmsDbContext db,
        DbSet<T> set,
        Guid id,
        DateTimeOffset nowUtc,
        CancellationToken ct) where T : class
    {
        var prop = typeof(T).GetProperty("LastStaleAlertDismissedAtUtc");
        if (prop is null) return false;
        var idProp = typeof(T).GetProperty("Id");
        if (idProp is null) return false;
        var rows = await set.ToListAsync(ct);
        var match = rows.FirstOrDefault(r => (Guid)idProp.GetValue(r)! == id);
        if (match is null) return false;
        prop.SetValue(match, nowUtc);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static async Task<IResult> ReassignOwnershipAsync(
        Guid id,
        [FromBody] ReassignOwnershipRequest? body,
        HttpContext context,
        [FromServices] CmsDbContext db,
        [FromServices] IAuditEventPublisher audit,
        CancellationToken ct)
    {
        if (!CmsResponseFactory.HasPermissionClaim(context, CmsPermissions.Publisher) &&
            !CmsResponseFactory.HasPermissionClaim(context, "super_admin"))
        {
            return CmsResponseFactory.Problem(context, 403,
                CmsReasonCode.PublishRoleRequired, "cms.publisher permission required.");
        }
        if (body is null || body.TargetActorId == Guid.Empty)
        {
            return CmsResponseFactory.Problem(context, 400,
                CmsReasonCode.PublishLocaleCompletenessMissing,
                "target_actor_id is required.");
        }
        var actorId = CmsResponseFactory.ResolveActorId(context);
        if (actorId is null)
        {
            return CmsResponseFactory.Problem(context, 401,
                CmsReasonCode.PublishRoleRequired, "Authenticated actor required.");
        }

        var found =
            await ReassignOneAsync(db, db.BannerSlots, id, body.TargetActorId, ct) ||
            await ReassignOneAsync(db, db.FeaturedSections, id, body.TargetActorId, ct) ||
            await ReassignOneAsync(db, db.FaqEntries, id, body.TargetActorId, ct) ||
            await ReassignOneAsync(db, db.BlogArticles, id, body.TargetActorId, ct) ||
            await ReassignOneAsync(db, db.LegalPageVersions, id, body.TargetActorId, ct);
        if (!found)
        {
            return CmsResponseFactory.Problem(context, 404,
                CmsReasonCode.PreviewEntityNotFound, "Draft not found.");
        }
        await audit.PublishAsync(new AuditEvent(
            ActorId: actorId.Value,
            ActorRole: CmsResponseFactory.ResolveActorRole(context),
            Action: "cms.draft.ownership_reassigned",
            EntityType: "cms.draft",
            EntityId: id,
            BeforeState: null,
            AfterState: new { NewOwnerActorId = body.TargetActorId },
            Reason: null), ct);
        return Results.NoContent();
    }

    private static async Task<bool> ReassignOneAsync<T>(
        CmsDbContext db, DbSet<T> set, Guid id, Guid newOwner, CancellationToken ct) where T : class
    {
        var ownerProp = typeof(T).GetProperty("OwnerActorId");
        var orphanProp = typeof(T).GetProperty("OwnershipOrphaned");
        var idProp = typeof(T).GetProperty("Id");
        if (ownerProp is null || orphanProp is null || idProp is null) return false;
        var rows = await set.ToListAsync(ct);
        var match = rows.FirstOrDefault(r => (Guid)idProp.GetValue(r)! == id);
        if (match is null) return false;
        ownerProp.SetValue(match, newOwner);
        orphanProp.SetValue(match, false);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string BuildKindReason(string kind, string suffix) =>
        $"cms.{kind.Replace('-', '_').Replace("slots", "slot").Replace("sections", "section").Replace("entries", "entry").Replace("articles", "article")}.{suffix}";
}
