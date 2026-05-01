using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Cms.Subscribers;

/// <summary>
/// Subscribes to <see cref="PricingCampaignDeactivated"/> from spec 007-b's
/// MediatR bus. Releases all active <c>BannerCampaignBinding</c> rows
/// pointing at the campaign with
/// <c>binding_state='released_due_to_campaign_deactivation'</c>; emits one
/// <c>cms.banner.campaign_unbound</c> audit per affected binding.
/// Idempotent — already-released bindings are skipped.
/// </summary>
public sealed class CampaignDeactivatedHandler : INotificationHandler<PricingCampaignDeactivated>
{
    private readonly CmsDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly ICmsCampaignBindingPublisher _bindingPublisher;
    private readonly TimeProvider _clock;
    private readonly ILogger<CampaignDeactivatedHandler> _log;

    public CampaignDeactivatedHandler(
        CmsDbContext db,
        IAuditEventPublisher audit,
        ICmsCampaignBindingPublisher bindingPublisher,
        TimeProvider clock,
        ILogger<CampaignDeactivatedHandler> log)
    {
        _db = db;
        _audit = audit;
        _bindingPublisher = bindingPublisher;
        _clock = clock;
        _log = log;
    }

    public async Task Handle(PricingCampaignDeactivated notification, CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow();
        var bindings = await _db.BannerCampaignBindings
            .Where(b => b.CampaignId == notification.CampaignId
                && b.BindingStateWire == "active")
            .ToListAsync(ct);

        if (bindings.Count == 0) return;

        foreach (var b in bindings)
        {
            b.BindingStateWire = "released_due_to_campaign_deactivation";
            b.ReleasedAtUtc = nowUtc;
            b.ReleaseReasonNote = "campaign deactivated upstream";
        }

        await _db.SaveChangesAsync(ct);

        foreach (var b in bindings)
        {
            await _audit.PublishAsync(new AuditEvent(
                ActorId: Guid.Empty,
                ActorRole: "system",
                Action: "cms.banner.campaign_unbound",
                EntityType: "cms.banner_campaign_binding",
                EntityId: b.Id,
                BeforeState: new { State = "active" },
                AfterState: new { State = "released_due_to_campaign_deactivation", b.ReleasedAtUtc },
                Reason: "pricing.campaign.deactivated"), ct);

            await _bindingPublisher.PublishUnboundAsync(new CmsBannerCampaignUnbound(
                BannerId: b.BannerId,
                CampaignId: b.CampaignId,
                ReleaseReasonWire: "released_due_to_campaign_deactivation",
                ActorId: null,
                ReleasedAtUtc: nowUtc), ct);
        }

        _log.LogInformation(
            "CampaignDeactivatedHandler: released {Count} banner-campaign bindings for campaign {CampaignId}.",
            bindings.Count, notification.CampaignId);
    }
}
