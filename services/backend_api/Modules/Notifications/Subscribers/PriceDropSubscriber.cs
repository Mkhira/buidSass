using System.Text.Json;
using BackendApi.Modules.Notifications.Primitives;
using MediatR;

namespace BackendApi.Modules.Notifications.Subscribers;

/// <summary>
/// PriceDropSubscriber — marketing-category event; the dispatch worker
/// re-checks per-customer marketing opt-in before sending. Quiet-hours
/// deferral (AC-22) is also a dispatch-layer concern.
/// </summary>
public sealed class PriceDropSubscriber : INotificationHandler<PricingPriceDropped>
{
    private readonly INotificationEnqueuer _enqueuer;

    public PriceDropSubscriber(INotificationEnqueuer enqueuer) { _enqueuer = enqueuer; }

    public Task Handle(PricingPriceDropped ev, CancellationToken ct) =>
        _enqueuer.EnqueueAsync(new EnqueueRequest(
            CorrelationId: ev.ProductId,
            RecipientId: ev.CustomerId,
            RecipientKind: NotificationsConstants.RecipientKinds.Customer,
            Channel: NotificationsConstants.Channels.Push,
            EventKind: NotificationsConstants.EventKinds.PricingPriceDropped,
            MarketCode: ev.MarketCode,
            Locale: ev.Locale,
            PayloadJson: JsonSerializer.Serialize(new
            {
                product_id = ev.ProductId,
                product_name = ev.ProductName,
                old_price = ev.OldPrice,
                new_price = ev.NewPrice,
                currency = ev.Currency,
            })), ct);
}
