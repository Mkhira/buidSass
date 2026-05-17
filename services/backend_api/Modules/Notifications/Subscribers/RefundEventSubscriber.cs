using System.Text.Json;
using BackendApi.Modules.Notifications.Primitives;
using MediatR;

namespace BackendApi.Modules.Notifications.Subscribers;

public sealed class RefundEventSubscriber :
    INotificationHandler<OrderRefundInitiated>,
    INotificationHandler<OrderRefundCompleted>
{
    private readonly INotificationEnqueuer _enqueuer;

    public RefundEventSubscriber(INotificationEnqueuer enqueuer) { _enqueuer = enqueuer; }

    public Task Handle(OrderRefundInitiated ev, CancellationToken ct) =>
        Enqueue(ev.OrderId, ev.CustomerId, NotificationsConstants.EventKinds.OrderRefundInitiated,
            ev.Locale, ev.MarketCode,
            new { order_number = ev.OrderNumber, amount = ev.RefundAmount, currency = ev.Currency }, ct);

    public Task Handle(OrderRefundCompleted ev, CancellationToken ct) =>
        Enqueue(ev.OrderId, ev.CustomerId, NotificationsConstants.EventKinds.OrderRefundCompleted,
            ev.Locale, ev.MarketCode,
            new { order_number = ev.OrderNumber, amount = ev.RefundAmount, currency = ev.Currency }, ct);

    private async Task Enqueue(Guid orderId, Guid customerId, string eventKind,
        string locale, string marketCode, object payload, CancellationToken ct)
    {
        await _enqueuer.EnqueueAsync(new EnqueueRequest(
            CorrelationId: orderId,
            RecipientId: customerId,
            RecipientKind: NotificationsConstants.RecipientKinds.Customer,
            Channel: NotificationsConstants.Channels.Email,
            EventKind: eventKind,
            MarketCode: marketCode,
            Locale: locale,
            PayloadJson: JsonSerializer.Serialize(payload)), ct);
    }
}
