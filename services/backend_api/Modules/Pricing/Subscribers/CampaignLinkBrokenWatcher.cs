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
/// </summary>
public sealed class CampaignLinkBrokenWatcher :
    INotificationHandler<CouponDeactivated>,
    INotificationHandler<CouponExpired>,
    INotificationHandler<PromotionDeactivated>,
    INotificationHandler<PromotionExpired>
{
    private readonly PricingDbContext _db;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;

    public CampaignLinkBrokenWatcher(
        PricingDbContext db,
        IPublisher publisher,
        TimeProvider time)
    {
        _db = db;
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

    private async Task HandleBrokenAsync(Guid targetId, string kind, CancellationToken ct)
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
        foreach (var c in campaigns)
        {
            if (!c.LinkBroken)
            {
                c.LinkBroken = true;
                c.UpdatedAt = nowUtc;
            }
        }

        await _db.SaveChangesAsync(ct);

        foreach (var c in campaigns)
        {
            await _publisher.Publish(new CampaignLinkBroken(
                c.Id, targetId, kind, nowUtc), ct);
        }
    }
}
