using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Editor.SaveBlogArticleDraft;

public sealed class SaveBlogArticleDraftHandler
{
    private readonly CmsDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public SaveBlogArticleDraftHandler(CmsDbContext db, IAuditEventPublisher audit, TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<SaveBlogArticleDraftResult> HandleAsync(
        Guid? id,
        Guid actorId,
        string actorRole,
        SaveBlogArticleDraftRequest body,
        CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow();
        BlogArticle row;
        var isCreate = id is null;

        if (isCreate)
        {
            row = new BlogArticle
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = nowUtc,
                StateWire = ContentLifecycleState.Draft.ToWire(),
                OwnerActorId = actorId,
            };
        }
        else
        {
            var loaded = await _db.BlogArticles.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (loaded is null)
            {
                return SaveBlogArticleDraftResult.Reject(
                    CmsReasonCode.PreviewEntityNotFound, "Blog article not found.", 404);
            }
            if (loaded.StateWire != ContentLifecycleState.Draft.ToWire())
            {
                return SaveBlogArticleDraftResult.Reject(
                    CmsReasonCode.DraftNotEditable,
                    "Only draft blog articles are editable.", 400);
            }
            if (body.Xmin is null || loaded.Xmin != body.Xmin.Value)
            {
                return SaveBlogArticleDraftResult.Reject(
                    CmsReasonCode.DraftVersionConflict,
                    "Blog article row was modified since it was loaded.", 409);
            }
            row = loaded;
        }

        // Slug uniqueness per (market_code, authored_locale).
        if (isCreate || row.Slug != body.Slug || row.MarketCode != body.MarketCode || row.AuthoredLocale != body.AuthoredLocale)
        {
            var collision = await _db.BlogArticles.AnyAsync(b =>
                b.Id != row.Id &&
                b.MarketCode == body.MarketCode &&
                b.AuthoredLocale == body.AuthoredLocale &&
                b.Slug == body.Slug, ct);
            if (collision)
            {
                return SaveBlogArticleDraftResult.Reject(
                    CmsReasonCode.BlogSlugCollision,
                    $"slug '{body.Slug}' already used in {body.MarketCode}/{body.AuthoredLocale}.",
                    400);
            }
        }

        ApplyToRow(row, body, nowUtc);

        if (isCreate) await _db.BlogArticles.AddAsync(row, ct);
        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: isCreate ? "cms.draft.created" : "cms.draft.updated",
            EntityType: "cms.blog_article",
            EntityId: row.Id,
            BeforeState: null,
            AfterState: new { row.Id, row.Slug, row.MarketCode, row.AuthoredLocale, row.StateWire, row.Xmin },
            Reason: null), ct);

        return isCreate ? SaveBlogArticleDraftResult.Created(row) : SaveBlogArticleDraftResult.Updated(row);
    }

    private static void ApplyToRow(BlogArticle row, SaveBlogArticleDraftRequest body, DateTimeOffset nowUtc)
    {
        row.CategoryWire = body.Category;
        row.Slug = body.Slug;
        row.AuthoredLocale = body.AuthoredLocale;
        row.Title = body.Title;
        row.Summary = body.Summary;
        row.Body = body.Body;
        row.CoverAssetId = body.CoverAssetId;
        row.SeoMetaTitle = body.SeoMetaTitle;
        row.SeoMetaDescription = body.SeoMetaDescription;
        row.SeoOgImageId = body.SeoOgImageId;
        row.SeoSchemaOrgKind = string.IsNullOrWhiteSpace(body.SeoSchemaOrgKind) ? "BlogPosting" : body.SeoSchemaOrgKind;
        row.ScheduledPublishAtUtc = body.ScheduledPublishAtUtc;
        row.MarketCode = body.MarketCode;
        row.EditorSaveAtUtc = nowUtc;
    }
}
