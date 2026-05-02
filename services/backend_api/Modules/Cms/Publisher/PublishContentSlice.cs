using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Cms.Services;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Publisher;

public sealed record PublishContentResult(
    bool IsSuccess,
    string? ReasonCode,
    string? Detail,
    int Status,
    object? Row,
    IDictionary<string, object?>? Extensions = null)
{
    public static PublishContentResult Ok(object row) => new(true, null, null, 200, row);
    public static PublishContentResult Reject(string reason, string detail, int status,
        IDictionary<string, object?>? extensions = null) =>
        new(false, reason, detail, status, null, extensions);
}

/// <summary>
/// Publish / schedule / archive for featured sections and FAQ entries. Banner
/// + blog + legal page slices have their own dedicated handlers; this one
/// covers the two simple shared-shape kinds.
/// </summary>
public sealed class PublishContentSlice
{
    private readonly CmsDbContext _db;
    private readonly FeaturedSectionResolver _featuredResolver;
    private readonly IAuditEventPublisher _audit;
    private readonly IPublisher _bus;
    private readonly TimeProvider _clock;

    public PublishContentSlice(
        CmsDbContext db,
        FeaturedSectionResolver featuredResolver,
        IAuditEventPublisher audit,
        IPublisher bus,
        TimeProvider clock)
    {
        _db = db;
        _featuredResolver = featuredResolver;
        _audit = audit;
        _bus = bus;
        _clock = clock;
    }

    public async Task<PublishContentResult> PublishFeaturedSectionNowAsync(
        Guid id, Guid actorId, string actorRole, bool schedule, CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow();
        var row = await _db.FeaturedSections.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (row is null)
        {
            return PublishContentResult.Reject(
                CmsReasonCode.PreviewEntityNotFound, "Featured section not found.", 404);
        }
        var sourceState = ContentLifecycleStateWire.FromWire(row.StateWire);
        if (sourceState != ContentLifecycleState.Draft && sourceState != ContentLifecycleState.Scheduled)
        {
            return PublishContentResult.Reject(
                CmsReasonCode.DraftNotEditable,
                "Only draft or scheduled featured sections can be published.", 400);
        }

        // Reference count check feeds the gate; 0 references is rejected
        // explicitly below with a more specific reason code.
        var gate = LocaleCompletenessGate.CheckFeaturedSection(
            row.TitleAr, row.TitleEn, referencesCount: 1);
        if (!gate.IsAllowed)
        {
            return PublishContentResult.Reject(
                gate.ReasonCode!,
                "Featured section is missing required bilingual fields.", 400,
                new Dictionary<string, object?> { ["missing_fields"] = gate.MissingFields });
        }

        // At least one reference must resolve at publish time.
        var resolved = await _featuredResolver.ResolveAsync(row, "en", ct);
        if (resolved.TotalReferences == 0)
        {
            return PublishContentResult.Reject(
                CmsReasonCode.FeaturedSectionEmptyReferences,
                "Featured section has no references.", 400);
        }
        if (resolved.TotalResolved == 0)
        {
            return PublishContentResult.Reject(
                CmsReasonCode.FeaturedSectionEmptyReferences,
                "All featured-section references are unavailable; cannot publish.", 400);
        }

        if (schedule)
        {
            if (row.ScheduledPublishAtUtc is null || row.ScheduledPublishAtUtc <= nowUtc)
            {
                return PublishContentResult.Reject(
                    CmsReasonCode.PublishEffectiveAtRequired,
                    "scheduled_publish_at_utc must be set in the future.", 400);
            }
            CmsContentLifecycle.AssertCanTransition(
                sourceState, ContentLifecycleState.Scheduled,
                EntityKind.FeaturedSection, CmsTriggerKind.PublisherSchedule, CmsActorKind.Publisher);
            row.StateWire = ContentLifecycleState.Scheduled.ToWire();
        }
        else
        {
            CmsContentLifecycle.AssertCanTransition(
                sourceState, ContentLifecycleState.Live,
                EntityKind.FeaturedSection, CmsTriggerKind.PublisherPublishNow, CmsActorKind.Publisher);
            row.StateWire = ContentLifecycleState.Live.ToWire();
            row.PublishedAtUtc = nowUtc;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: schedule ? "cms.content.scheduled" : "cms.content.published",
            EntityType: "cms.featured_section",
            EntityId: row.Id,
            BeforeState: new { State = sourceState.ToWire() },
            AfterState: new { State = row.StateWire, row.PublishedAtUtc },
            Reason: null), ct);

        if (!schedule)
        {
            await _bus.Publish(new CmsFeaturedSectionPublished(
                EventId: Guid.NewGuid(),
                OccurredAtUtc: nowUtc,
                ActorId: actorId,
                EntityId: row.Id,
                VersionId: row.Id,
                MarketCode: row.MarketCode,
                SectionKindWire: row.SectionKindWire), ct);
            await _bus.Publish(new CmsCacheInvalidateFeaturedSection(
                EventId: Guid.NewGuid(),
                OccurredAtUtc: nowUtc,
                EntityId: row.Id,
                VersionId: row.Id,
                MarketCode: row.MarketCode), ct);
        }

        return PublishContentResult.Ok(row);
    }

    public async Task<PublishContentResult> PublishFaqEntryNowAsync(
        Guid id, Guid actorId, string actorRole, bool schedule, CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow();
        var row = await _db.FaqEntries.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (row is null)
        {
            return PublishContentResult.Reject(
                CmsReasonCode.PreviewEntityNotFound, "FAQ entry not found.", 404);
        }
        var sourceState = ContentLifecycleStateWire.FromWire(row.StateWire);
        if (sourceState != ContentLifecycleState.Draft && sourceState != ContentLifecycleState.Scheduled)
        {
            return PublishContentResult.Reject(
                CmsReasonCode.DraftNotEditable,
                "Only draft or scheduled FAQ entries can be published.", 400);
        }

        var gate = LocaleCompletenessGate.CheckFaqEntry(
            row.QuestionAr, row.QuestionEn, row.AnswerAr, row.AnswerEn);
        if (!gate.IsAllowed)
        {
            return PublishContentResult.Reject(
                gate.ReasonCode!,
                "FAQ entry is missing required bilingual fields.", 400,
                new Dictionary<string, object?> { ["missing_fields"] = gate.MissingFields });
        }

        if (schedule)
        {
            if (row.ScheduledPublishAtUtc is null || row.ScheduledPublishAtUtc <= nowUtc)
            {
                return PublishContentResult.Reject(
                    CmsReasonCode.PublishEffectiveAtRequired,
                    "scheduled_publish_at_utc must be set in the future.", 400);
            }
            CmsContentLifecycle.AssertCanTransition(
                sourceState, ContentLifecycleState.Scheduled,
                EntityKind.FaqEntry, CmsTriggerKind.PublisherSchedule, CmsActorKind.Publisher);
            row.StateWire = ContentLifecycleState.Scheduled.ToWire();
        }
        else
        {
            CmsContentLifecycle.AssertCanTransition(
                sourceState, ContentLifecycleState.Live,
                EntityKind.FaqEntry, CmsTriggerKind.PublisherPublishNow, CmsActorKind.Publisher);
            row.StateWire = ContentLifecycleState.Live.ToWire();
            row.PublishedAtUtc = nowUtc;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: schedule ? "cms.content.scheduled" : "cms.content.published",
            EntityType: "cms.faq_entry",
            EntityId: row.Id,
            BeforeState: new { State = sourceState.ToWire() },
            AfterState: new { State = row.StateWire, row.PublishedAtUtc },
            Reason: null), ct);

        if (!schedule)
        {
            await _bus.Publish(new CmsFaqPublished(
                EventId: Guid.NewGuid(),
                OccurredAtUtc: nowUtc,
                ActorId: actorId,
                EntityId: row.Id,
                VersionId: row.Id,
                MarketCode: row.MarketCode,
                CategoryWire: row.CategoryWire), ct);
            await _bus.Publish(new CmsCacheInvalidateFaq(
                EventId: Guid.NewGuid(),
                OccurredAtUtc: nowUtc,
                EntityId: row.Id,
                VersionId: row.Id,
                MarketCode: row.MarketCode), ct);
        }

        return PublishContentResult.Ok(row);
    }

}
