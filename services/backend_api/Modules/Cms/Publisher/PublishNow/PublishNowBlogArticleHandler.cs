using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Publisher.PublishNow;

public sealed record BlogPublisherResult(
    bool IsSuccess,
    BlogArticle? Row,
    string? ReasonCode,
    string? Detail,
    int Status,
    IDictionary<string, object?>? Extensions = null)
{
    public static BlogPublisherResult Ok(BlogArticle row) => new(true, row, null, null, 200);
    public static BlogPublisherResult Reject(string reason, string detail, int status,
        IDictionary<string, object?>? extensions = null) =>
        new(false, null, reason, detail, status, extensions);
}

/// <summary>
/// POST /v1/admin/cms/blog-articles/{id}/publish-now per spec 024 contract §4.
/// Single-locale articles ARE allowed (Principle 4 long-form rule); locale gate
/// only requires the authored body. Schedule path uses the same handler with
/// scheduled_publish_at_utc set. Promotes draft|scheduled → live.
/// </summary>
public sealed class PublishNowBlogArticleHandler
{
    private readonly CmsDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly IPublisher _bus;
    private readonly TimeProvider _clock;

    public PublishNowBlogArticleHandler(
        CmsDbContext db,
        IAuditEventPublisher audit,
        IPublisher bus,
        TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _bus = bus;
        _clock = clock;
    }

    public async Task<BlogPublisherResult> HandleAsync(
        Guid blogId,
        Guid actorId,
        string actorRole,
        bool schedule,
        CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow();
        var row = await _db.BlogArticles.FirstOrDefaultAsync(b => b.Id == blogId, ct);
        if (row is null)
        {
            return BlogPublisherResult.Reject(CmsReasonCode.PreviewEntityNotFound, "Blog article not found.", 404);
        }

        var sourceState = ContentLifecycleStateWire.FromWire(row.StateWire);
        if (sourceState != ContentLifecycleState.Draft && sourceState != ContentLifecycleState.Scheduled)
        {
            return BlogPublisherResult.Reject(CmsReasonCode.DraftNotEditable,
                "Only draft or scheduled blog articles can be published.", 400);
        }

        var gate = LocaleCompletenessGate.CheckBlogArticle(
            row.AuthoredLocale, row.Body, row.SeoMetaTitle, row.SeoMetaDescription);
        if (!gate.IsAllowed)
        {
            return BlogPublisherResult.Reject(gate.ReasonCode!,
                "Blog article is missing required fields.", 400,
                new Dictionary<string, object?> { ["missing_fields"] = gate.MissingFields });
        }

        if (schedule)
        {
            if (row.ScheduledPublishAtUtc is null || row.ScheduledPublishAtUtc <= nowUtc)
            {
                return BlogPublisherResult.Reject(
                    CmsReasonCode.PublishEffectiveAtRequired,
                    "scheduled_publish_at_utc must be set in the future.", 400);
            }
            CmsContentLifecycle.AssertCanTransition(
                sourceState, ContentLifecycleState.Scheduled,
                EntityKind.BlogArticle, CmsTriggerKind.PublisherSchedule, CmsActorKind.Publisher);
            row.StateWire = ContentLifecycleState.Scheduled.ToWire();
        }
        else
        {
            CmsContentLifecycle.AssertCanTransition(
                sourceState, ContentLifecycleState.Live,
                EntityKind.BlogArticle, CmsTriggerKind.PublisherPublishNow, CmsActorKind.Publisher);
            row.StateWire = ContentLifecycleState.Live.ToWire();
            row.PublishedAtUtc = nowUtc;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: schedule ? "cms.content.scheduled" : "cms.content.published",
            EntityType: "cms.blog_article",
            EntityId: row.Id,
            BeforeState: new { State = sourceState.ToWire() },
            AfterState: new { State = row.StateWire, row.PublishedAtUtc, row.ScheduledPublishAtUtc },
            Reason: null), ct);

        if (!schedule)
        {
            await _bus.Publish(new CmsBlogArticlePublished(
                EventId: Guid.NewGuid(),
                OccurredAtUtc: nowUtc,
                ActorId: actorId,
                EntityId: row.Id,
                VersionId: row.Id,
                MarketCode: row.MarketCode,
                AuthoredLocale: row.AuthoredLocale,
                CategoryWire: row.CategoryWire), ct);

            await _bus.Publish(new CmsCacheInvalidateBlogArticle(
                EventId: Guid.NewGuid(),
                OccurredAtUtc: nowUtc,
                EntityId: row.Id,
                VersionId: row.Id,
                MarketCode: row.MarketCode,
                Slug: row.Slug), ct);
        }

        return BlogPublisherResult.Ok(row);
    }
}
