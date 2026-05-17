using System.Text.Json;
using BackendApi.Modules.Notifications.Primitives;
using MediatR;

namespace BackendApi.Modules.Notifications.Subscribers;

public sealed class AbandonedCartSubscriber : INotificationHandler<CartAbandoned24h>
{
    private readonly INotificationEnqueuer _enqueuer;

    public AbandonedCartSubscriber(INotificationEnqueuer enqueuer) { _enqueuer = enqueuer; }

    public Task Handle(CartAbandoned24h ev, CancellationToken ct) =>
        _enqueuer.EnqueueAsync(new EnqueueRequest(
            CorrelationId: ev.CartId,
            RecipientId: ev.CustomerId,
            RecipientKind: NotificationsConstants.RecipientKinds.Customer,
            Channel: NotificationsConstants.Channels.Email,
            EventKind: NotificationsConstants.EventKinds.CartAbandoned24h,
            MarketCode: ev.MarketCode,
            Locale: ev.Locale,
            PayloadJson: JsonSerializer.Serialize(new
            {
                cart_id = ev.CartId,
                item_count = ev.ItemCount,
                cart_total = ev.CartTotal,
                currency = ev.Currency,
            })), ct);
}
