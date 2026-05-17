using System.Text.Json;
using BackendApi.Modules.Notifications.Primitives;
using MediatR;

namespace BackendApi.Modules.Notifications.Subscribers;

/// <summary>
/// T027 — OTP delivery is the only path that targets the OTP-priority queue
/// (BR-15). The subscriber materializes a single notification on the
/// requested channel (SMS or email); queue isolation is enforced downstream
/// by <c>OtpDispatchWorker</c>, which reads only rows whose
/// <see cref="Domain.Notification.EventKind"/> equals
/// <see cref="NotificationsConstants.EventKinds.AuthOtpRequested"/>.
/// </summary>
public sealed class OtpRequestedSubscriber : INotificationHandler<AuthOtpRequested>
{
    private readonly INotificationEnqueuer _enqueuer;

    public OtpRequestedSubscriber(INotificationEnqueuer enqueuer)
    {
        _enqueuer = enqueuer;
    }

    public async Task Handle(AuthOtpRequested ev, CancellationToken cancellationToken)
    {
        // The OTP value itself is NOT included in the redacted payload — only
        // the channel, ttl, and the last-2 of the code are surfaced for audit.
        // The dispatch worker re-fetches the OTP from the auth module's
        // short-lived store at send-time, never persisting it here.
        var payload = JsonSerializer.Serialize(new
        {
            channel = ev.Channel,
            ttl_seconds = ev.TtlSeconds,
            code_tail = ev.OtpCode.Length >= 2 ? ev.OtpCode[^2..] : "**",
        });

        await _enqueuer.EnqueueAsync(new EnqueueRequest(
            CorrelationId: Guid.NewGuid(),
            RecipientId: ev.CustomerId,
            RecipientKind: NotificationsConstants.RecipientKinds.Customer,
            Channel: ev.Channel,
            EventKind: NotificationsConstants.EventKinds.AuthOtpRequested,
            MarketCode: ev.MarketCode,
            Locale: ev.Locale,
            PayloadJson: payload), cancellationToken);
    }
}
