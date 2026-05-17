using System.Text.Json;
using BackendApi.Modules.Notifications.Primitives;
using MediatR;

namespace BackendApi.Modules.Notifications.Subscribers;

public sealed class ShippingStatusSubscriber : INotificationHandler<ShippingStatusChanged>
{
    private readonly INotificationEnqueuer _enqueuer;

    public ShippingStatusSubscriber(INotificationEnqueuer enqueuer) { _enqueuer = enqueuer; }

    public Task Handle(ShippingStatusChanged ev, CancellationToken ct) =>
        _enqueuer.EnqueueAsync(new EnqueueRequest(
            CorrelationId: ev.OrderId,
            RecipientId: ev.CustomerId,
            RecipientKind: NotificationsConstants.RecipientKinds.Customer,
            Channel: NotificationsConstants.Channels.Email,
            EventKind: NotificationsConstants.EventKinds.ShippingStatusChanged,
            MarketCode: ev.MarketCode,
            Locale: ev.Locale,
            PayloadJson: JsonSerializer.Serialize(new
            {
                order_number = ev.OrderNumber,
                status = ev.StatusCode,
                carrier_message = ev.CarrierMessage,
            })), ct);
}
