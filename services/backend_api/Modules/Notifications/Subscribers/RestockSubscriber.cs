using System.Text.Json;
using BackendApi.Modules.Notifications.Primitives;
using MediatR;

namespace BackendApi.Modules.Notifications.Subscribers;

public sealed class RestockSubscriber : INotificationHandler<InventoryRestocked>
{
    private readonly INotificationEnqueuer _enqueuer;

    public RestockSubscriber(INotificationEnqueuer enqueuer) { _enqueuer = enqueuer; }

    public Task Handle(InventoryRestocked ev, CancellationToken ct) =>
        _enqueuer.EnqueueAsync(new EnqueueRequest(
            CorrelationId: ev.ProductId,
            RecipientId: ev.CustomerId,
            RecipientKind: NotificationsConstants.RecipientKinds.Customer,
            Channel: NotificationsConstants.Channels.Push,
            EventKind: NotificationsConstants.EventKinds.InventoryRestocked,
            MarketCode: ev.MarketCode,
            Locale: ev.Locale,
            PayloadJson: JsonSerializer.Serialize(new
            {
                product_id = ev.ProductId,
                product_name = ev.ProductName,
            })), ct);
}
