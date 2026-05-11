using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Pricing.Subscribers;

/// <summary>
/// Spec 007-b T131 — listens to Coupon/Promotion deactivation and expiration
/// events and marks any Campaign whose <c>CampaignLink.TargetId</c> equals the
/// broken rule's id with <c>link_broken=true</c> + emits
/// <see cref="CampaignLinkBroken"/> (FR-019).
///
/// The Campaign itself MUST NOT auto-deactivate — only the broken-link
/// indicator is surfaced. Operator action is required.
///
/// CodeRabbit PR #81 round 1 Major: a <c>campaign.updated</c> audit row is
/// persisted for every broken-link mutation (Principle 25) so
/// <c>/campaigns/{id}</c> exposes the cause via <c>audit_summary</c>.
/// </summary>
public sealed class CampaignLinkBrokenWatcher :
    INotificationHandler<CouponDeactivated>,
    INotificationHandler<CouponExpired>,
    INotificationHandler<PromotionDeactivated>,
    INotificationHandler<PromotionExpired>
{
    private static readonly System.Guid SystemActorId = CommercialPermissions.SystemActorId;
    private const string SystemActorRole = "system";

    private readonly PricingDbContext _db;
    private readonly CommercialAuditWriter _audit;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;

    public CampaignLinkBrokenWatcher(
        PricingDbContext db,
        CommercialAuditWriter audit,
        IPublisher publisher,
        TimeProvider time)
    {
        _db = db;
        _audit = audit;
        _publisher = publisher;
        _time = time;
    }

    public Task Handle(CouponDeactivated notification, CancellationToken ct)
        => HandleBrokenAsync(notification.CouponId, "coupon", ct);

    public Task Handle(CouponExpired notification, CancellationToken ct)
        => HandleBrokenAsync(notification.CouponId, "coupon", ct);

    public Task Handle(PromotionDeactivated notification, CancellationToken ct)
        => HandleBrokenAsync(notification.PromotionId, "promotion", ct);

    public Task Handle(PromotionExpired notification, CancellationToken ct)
        => HandleBrokenAsync(notification.PromotionId, "promotion", ct);

    private async Task HandleBrokenAsync(System.Guid targetId, string kind, CancellationToken ct)
    {
        var nowUtc = _time.GetUtcNow();

        // Find every active campaign_link pointing at the broken rule.
        var links = await _db.CampaignLinks
            .Where(l => l.TargetId == targetId &&
                        l.Kind == kind &&
                        l.LinkBrokenAtUtc == null)
            .ToListAsync(ct);
        if (links.Count == 0) return;

        var campaignIds = links.Select(l => l.CampaignId).Distinct().ToList();
        var campaigns = await _db.Campaigns
            .Where(c => campaignIds.Contains(c.Id))
            .ToListAsync(ct);

        foreach (var link in links)
        {
            link.LinkBrokenAtUtc = nowUtc;
        }

        var flippedCampaigns = new List<Entities.Campaign>();
        foreach (var c in campaigns)
        {
            if (!c.LinkBroken)
            {
                c.LinkBroken = true;
                c.UpdatedAt = nowUtc;
                flippedCampaigns.Add(c);
            }
        }

        // Stage one audit row per campaign whose flag actually flipped this tick.
        // Idempotent — re-delivery against an already-broken campaign writes no
        // duplicate audit row.
        var auditPublishers = new List<Func<CancellationToken, Task>>();
        foreach (var c in flippedCampaigns)
        {
            auditPublishers.Add(_audit.StageLocal(
                "campaign", c.Id, "campaign.updated",
                SystemActorId, SystemActorRole,
                before: new { link_broken = false },
                after: new { link_broken = true },
                diff: new { broken_link_target_id = targetId, broken_link_kind = kind, via = "watcher" },
                reasonNote: $"linked {kind} {targetId} deactivated/expired",
                correlationId: null,
                nowUtc));
        }

        await _db.SaveChangesAsync(ct);
        foreach (var publish in auditPublishers)
        {
            await publish(ct);
        }

        foreach (var c in campaigns)
        {
            await _publisher.Publish(new CampaignLinkBroken(
                c.Id, targetId, kind, nowUtc), ct);
        }
    }
}
