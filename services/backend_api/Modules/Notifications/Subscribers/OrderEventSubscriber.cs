using System.Text.Json;
using BackendApi.Modules.Notifications.Primitives;
using MediatR;

namespace BackendApi.Modules.Notifications.Subscribers;

/// <summary>
/// T028 — five order events (placed, confirmed, shipped, delivered, cancelled).
/// Each enqueues an email notification by default. SMS / push are layered in
/// later phases when <see cref="Domain.Preference"/> says the customer opted
/// into multi-channel for order status. Per AC-8: two deliveries within 60s
/// for a placed-then-confirmed sequence are the load-bearing acceptance.
/// </summary>
public sealed class OrderEventSubscriber :
    INotificationHandler<OrderPlaced>,
    INotificationHandler<OrderConfirmed>,
    INotificationHandler<OrderShipped>,
    INotificationHandler<OrderDelivered>,
    INotificationHandler<OrderCancelled>
{
    private readonly INotificationEnqueuer _enqueuer;

    public OrderEventSubscriber(INotificationEnqueuer enqueuer)
    {
        _enqueuer = enqueuer;
    }

    public Task Handle(OrderPlaced ev, CancellationToken ct) =>
        EnqueueOrderEmail(
            correlationId: ev.OrderId,
            customerId: ev.CustomerId,
            eventKind: NotificationsConstants.EventKinds.OrderPlaced,
            locale: ev.Locale,
            marketCode: ev.MarketCode,
            payload: new { order_number = ev.OrderNumber, total = ev.TotalAmount, currency = ev.Currency },
            ct: ct);

    public Task Handle(OrderConfirmed ev, CancellationToken ct) =>
        EnqueueOrderEmail(ev.OrderId, ev.CustomerId,
            NotificationsConstants.EventKinds.OrderConfirmed, ev.Locale, ev.MarketCode,
            new { order_number = ev.OrderNumber }, ct);

    public Task Handle(OrderShipped ev, CancellationToken ct) =>
        EnqueueOrderEmail(ev.OrderId, ev.CustomerId,
            NotificationsConstants.EventKinds.OrderShipped, ev.Locale, ev.MarketCode,
            new { order_number = ev.OrderNumber, carrier = ev.Carrier, tracking = ev.TrackingNumber }, ct);

    public Task Handle(OrderDelivered ev, CancellationToken ct) =>
        EnqueueOrderEmail(ev.OrderId, ev.CustomerId,
            NotificationsConstants.EventKinds.OrderDelivered, ev.Locale, ev.MarketCode,
            new { order_number = ev.OrderNumber }, ct);

    public Task Handle(OrderCancelled ev, CancellationToken ct) =>
        EnqueueOrderEmail(ev.OrderId, ev.CustomerId,
            NotificationsConstants.EventKinds.OrderCancelled, ev.Locale, ev.MarketCode,
            new { order_number = ev.OrderNumber, reason = ev.CancellationReason }, ct);

    private async Task EnqueueOrderEmail(
        Guid correlationId,
        Guid customerId,
        string eventKind,
        string locale,
        string marketCode,
        object payload,
        CancellationToken ct)
    {
        await _enqueuer.EnqueueAsync(new EnqueueRequest(
            CorrelationId: correlationId,
            RecipientId: customerId,
            RecipientKind: NotificationsConstants.RecipientKinds.Customer,
            Channel: NotificationsConstants.Channels.Email,
            EventKind: eventKind,
            MarketCode: marketCode,
            Locale: locale,
            PayloadJson: JsonSerializer.Serialize(payload)), ct);
    }
}
