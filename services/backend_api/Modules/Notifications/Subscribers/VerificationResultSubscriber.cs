using System.Text.Json;
using BackendApi.Modules.Notifications.Primitives;
using MediatR;

namespace BackendApi.Modules.Notifications.Subscribers;

public sealed class VerificationResultSubscriber :
    INotificationHandler<VerificationApproved>,
    INotificationHandler<VerificationRejected>
{
    private readonly INotificationEnqueuer _enqueuer;

    public VerificationResultSubscriber(INotificationEnqueuer enqueuer) { _enqueuer = enqueuer; }

    public Task Handle(VerificationApproved ev, CancellationToken ct) =>
        _enqueuer.EnqueueAsync(new EnqueueRequest(
            CorrelationId: ev.VerificationId,
            RecipientId: ev.CustomerId,
            RecipientKind: NotificationsConstants.RecipientKinds.Customer,
            Channel: NotificationsConstants.Channels.Email,
            EventKind: NotificationsConstants.EventKinds.VerificationApproved,
            MarketCode: ev.MarketCode,
            Locale: ev.Locale,
            PayloadJson: JsonSerializer.Serialize(new { verification_id = ev.VerificationId })), ct);

    public Task Handle(VerificationRejected ev, CancellationToken ct) =>
        _enqueuer.EnqueueAsync(new EnqueueRequest(
            CorrelationId: ev.VerificationId,
            RecipientId: ev.CustomerId,
            RecipientKind: NotificationsConstants.RecipientKinds.Customer,
            Channel: NotificationsConstants.Channels.Email,
            EventKind: NotificationsConstants.EventKinds.VerificationRejected,
            MarketCode: ev.MarketCode,
            Locale: ev.Locale,
            PayloadJson: JsonSerializer.Serialize(new { verification_id = ev.VerificationId, reason = ev.ReasonCode })), ct);
}
