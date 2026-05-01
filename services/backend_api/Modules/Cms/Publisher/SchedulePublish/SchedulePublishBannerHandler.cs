using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Cms.Services;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Publisher.SchedulePublish;

/// <summary>
/// POST /v1/admin/cms/banner-slots/{id}/schedule-publish per spec 024
/// contract §4.2. Runs the publish gates (locale-completeness, banner CTA
/// validation, banner capacity) and transitions the row from
/// <c>draft</c> to <c>scheduled</c>. The
/// <see cref="Workers.CmsScheduledPublishWorker"/> later promotes it to
/// <c>live</c> at <c>scheduled_start_utc</c>.
/// </summary>
public sealed class SchedulePublishBannerHandler
{
    private readonly CmsDbContext _db;
    private readonly BannerCapacityCalculator _capacity;
    private readonly BannerCtaValidator _ctaValidator;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public SchedulePublishBannerHandler(
        CmsDbContext db,
        BannerCapacityCalculator capacity,
        BannerCtaValidator ctaValidator,
        IAuditEventPublisher audit,
        TimeProvider clock)
    {
        _db = db;
        _capacity = capacity;
        _ctaValidator = ctaValidator;
        _audit = audit;
        _clock = clock;
    }

    public async Task<PublisherResult> HandleAsync(
        Guid bannerId,
        Guid actorId,
        string actorRole,
        CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow();
        var row = await _db.BannerSlots.FirstOrDefaultAsync(b => b.Id == bannerId, ct);
        if (row is null)
        {
            return PublisherResult.Reject(CmsReasonCode.PreviewEntityNotFound, "Banner not found.", 404);
        }

        if (row.StateWire != ContentLifecycleState.Draft.ToWire())
        {
            return PublisherResult.Reject(CmsReasonCode.DraftNotEditable,
                "Only draft banners can be scheduled.", 400);
        }

        if (row.ScheduledStartUtc is null || row.ScheduledEndUtc is null)
        {
            return PublisherResult.Reject(CmsReasonCode.BannerScheduleWindowInvalid,
                "Both scheduled_start_utc and scheduled_end_utc are required at schedule-publish.", 400);
        }

        // 1) Locale-completeness gate.
        var localeCheck = LocaleCompletenessGate.CheckBanner(
            row.HeadlineAr, row.HeadlineEn, row.AssetIdAr, row.AssetIdEn);
        if (!localeCheck.IsAllowed)
        {
            return PublisherResult.Reject(localeCheck.ReasonCode!,
                "Banner is missing required bilingual fields.", 400,
                new Dictionary<string, object?> { ["missing_fields"] = localeCheck.MissingFields });
        }

        // 2) Banner CTA validation (hard-fail mode).
        try
        {
            await _ctaValidator.ValidateForPublishAsync(row, ct);
        }
        catch (BannerCtaTargetUnresolvableException ex)
        {
            return PublisherResult.Reject(ex.ReasonCode, "Banner CTA target cannot be resolved.", 400,
                new Dictionary<string, object?>
                {
                    ["cta_kind"] = ex.CtaKind,
                    ["cta_target"] = ex.CtaTarget,
                });
        }

        // 3) Banner capacity check.
        var policy = await ResolvePolicyAsync(row.MarketCode, ct);
        try
        {
            await _capacity.AssertCanPublishAsync(
                _db.BannerSlots.AsQueryable(),
                BannerSlotKindWire.FromWire(row.SlotKindWire),
                row.MarketCode,
                policy,
                nowUtc,
                ct);
        }
        catch (BannerSlotCapacityExceededException ex)
        {
            return PublisherResult.Reject(ex.ReasonCode, "Banner slot capacity exceeded.", 400,
                new Dictionary<string, object?>
                {
                    ["slot_kind"] = ex.SlotKind.ToWire(),
                    ["market_code"] = ex.MarketCode,
                    ["current_live_count"] = ex.CurrentLiveCount,
                    ["cap"] = ex.Cap,
                });
        }

        // 4) State transition guard (compile-time + runtime).
        CmsContentLifecycle.AssertCanTransition(
            ContentLifecycleState.Draft, ContentLifecycleState.Scheduled,
            EntityKind.BannerSlot, CmsTriggerKind.PublisherSchedule, CmsActorKind.Publisher);

        row.StateWire = ContentLifecycleState.Scheduled.ToWire();
        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: "cms.content.scheduled",
            EntityType: "cms.banner_slot",
            EntityId: row.Id,
            BeforeState: new { State = "draft" },
            AfterState: new { State = "scheduled", row.ScheduledStartUtc, row.ScheduledEndUtc },
            Reason: null), ct);

        return PublisherResult.Ok(row);
    }

    private async Task<CmsMarketPolicy> ResolvePolicyAsync(string marketCode, CancellationToken ct)
    {
        var schema = await _db.MarketSchemas.FirstOrDefaultAsync(s => s.MarketCode == marketCode, ct);
        if (schema is null) return CmsMarketPolicy.Default(marketCode);
        return new CmsMarketPolicy(
            MarketCode: schema.MarketCode,
            BannerMaxLivePerSlot: schema.BannerMaxLivePerSlot,
            FeaturedSectionMaxReferences: schema.FeaturedSectionMaxReferences,
            PreviewTokenDefaultTtlHours: schema.PreviewTokenDefaultTtlHours,
            DraftStalenessAlertDays: schema.DraftStalenessAlertDays,
            AssetGracePeriodDays: schema.AssetGracePeriodDays);
    }
}
