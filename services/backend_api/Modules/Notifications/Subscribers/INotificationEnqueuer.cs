namespace BackendApi.Modules.Notifications.Subscribers;

/// <summary>
/// Internal orchestrator subscribers call to materialize a
/// <see cref="Domain.Notification"/> row from an upstream domain event.
/// Idempotent by SHA-256 of <c>(correlation_id, channel, recipient_id)</c>
/// — re-publishing the same upstream event is a no-op per BR-3.
///
/// Template selection + body rendering are deliberately deferred to the
/// dispatch worker (T030) so that subscribers stay fast (under 50ms) and
/// the OTP-priority queue isolation guarantee holds even when the template
/// renderer takes longer than the event-bus dispatch window.
/// </summary>
public interface INotificationEnqueuer
{
    /// <summary>
    /// Enqueue a notification for the given <paramref name="request"/>. Returns
    /// the existing notification id if an identical idempotency key is already
    /// present (no new row created).
    /// </summary>
    Task<Guid> EnqueueAsync(EnqueueRequest request, CancellationToken cancellationToken);
}

public sealed record EnqueueRequest(
    Guid CorrelationId,
    Guid? RecipientId,
    string RecipientKind,
    string Channel,
    string EventKind,
    string MarketCode,
    string Locale,
    string PayloadJson,
    Guid? CampaignId = null,
    DateTimeOffset? NotBefore = null);
