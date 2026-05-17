using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Notifications.Providers;

/// <summary>
/// Provider abstraction per <c>contracts/notifications-contract.md §5</c>.
/// Six concrete impls satisfy this surface: SES + SendGrid (email),
/// Unifonic + Infobip (KSA SMS), Vodafone Egypt + Infobip (EG SMS), FCM
/// (push). Business logic MUST NOT branch on <see cref="ProviderId"/>
/// outside this folder — that is Principle 13 + 14's substitution
/// guarantee.
/// </summary>
public interface INotificationProvider
{
    /// <summary>Canonical provider identifier (matches <c>NotificationsConstants.Providers</c>).</summary>
    string ProviderId { get; }

    /// <summary>Channel this provider serves: <c>sms</c>, <c>email</c>, or <c>push</c>.</summary>
    string Channel { get; }

    /// <summary>Returns <c>true</c> if the provider supports the given market.</summary>
    bool SupportsMarket(string marketCode);

    /// <summary>
    /// Sends a single notification. Idempotent on <see cref="NotificationDispatch.IdempotencyKey"/>.
    /// Implementations MUST classify transient (5xx, network, timeout) vs
    /// terminal (4xx other than 429) failures via <see cref="SendResult.IsTransient"/>.
    /// </summary>
    Task<SendResult> SendAsync(NotificationDispatch dispatch, CancellationToken cancellationToken);

    /// <summary>
    /// Validates inbound webhook signature using KV-stored secrets. Fail-closed
    /// — returns <c>false</c> on any malformed / missing / mismatched signature
    /// rather than throwing. The caller maps a <c>false</c> result to HTTP 401.
    /// </summary>
    bool ValidateWebhookSignature(HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets);

    /// <summary>
    /// Parses a verified webhook into the canonical <see cref="WebhookEvent"/>
    /// shape. Called only after <see cref="ValidateWebhookSignature"/> returns
    /// <c>true</c>.
    /// </summary>
    WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody);
}

/// <summary>
/// Payload sent to <see cref="INotificationProvider.SendAsync"/>. PII is
/// minimized per AC-27: the renderer redacts national IDs, masks phones to
/// last-4 in the audit copy, and never serializes raw card data. The fields
/// here represent the minimum surface area required to drive each channel.
/// </summary>
public sealed record NotificationDispatch(
    Guid NotificationId,
    string Channel,
    /// <summary>Email address (email), E.164 phone (sms), or FCM token (push).</summary>
    string Recipient,
    /// <summary>Empty for SMS/push, present for email.</summary>
    string Subject,
    /// <summary>Rendered, locale-correct body.</summary>
    string Body,
    string Locale,
    string MarketCode,
    /// <summary>SHA-256 idempotency key (matches <c>notifications.IdempotencyKey</c>).</summary>
    string IdempotencyKey,
    /// <summary>Tracing / idempotency / correlation headers.</summary>
    IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// Outcome of a single dispatch attempt. <see cref="IsTransient"/> drives the
/// retry policy per BR-4 (transient → backoff; terminal → fail / dead-letter).
/// </summary>
public sealed record SendResult(
    bool Accepted,
    string? ProviderMessageId,
    string? ErrorCode,
    string? ErrorMessageRedacted,
    bool IsTransient,
    int? StatusCode)
{
    public static SendResult Ok(string providerMessageId) =>
        new(Accepted: true,
            ProviderMessageId: providerMessageId,
            ErrorCode: null,
            ErrorMessageRedacted: null,
            IsTransient: false,
            StatusCode: 200);

    public static SendResult Transient(string errorCode, string errorRedacted, int statusCode) =>
        new(Accepted: false,
            ProviderMessageId: null,
            ErrorCode: errorCode,
            ErrorMessageRedacted: errorRedacted,
            IsTransient: true,
            StatusCode: statusCode);

    public static SendResult Terminal(string errorCode, string errorRedacted, int? statusCode) =>
        new(Accepted: false,
            ProviderMessageId: null,
            ErrorCode: errorCode,
            ErrorMessageRedacted: errorRedacted,
            IsTransient: false,
            StatusCode: statusCode);
}

/// <summary>
/// Canonical webhook event emitted by every provider's parser, mapped onto
/// <see cref="Domain.StateMachines.NotificationStateMachine"/>.
/// </summary>
public sealed record WebhookEvent(
    string ProviderId,
    string ProviderMessageId,
    /// <summary>Provider-specific raw event kind (for audit).</summary>
    string EventKind,
    /// <summary>Canonical kind (see <see cref="CanonicalWebhookEventKinds"/>).</summary>
    string CanonicalEventKind,
    DateTimeOffset OccurredAt,
    string? ErrorCode);

/// <summary>Canonical event kinds emitted by provider-specific parsers.</summary>
public static class CanonicalWebhookEventKinds
{
    public const string Accepted = "accepted";
    public const string Delivered = "delivered";
    public const string Bounced = "bounced";
    public const string SoftBounced = "soft_bounced";
    public const string Failed = "failed";
    public const string Unregistered = "unregistered";
    public const string Complaint = "complaint";
    /// <summary>
    /// Reserved for events the parser recognized as well-formed but cannot
    /// map onto a state transition (e.g. provider-internal notifications,
    /// new event kinds added after our adapter shipped). The webhook handler
    /// MUST record the receipt for audit + idempotency but MUST NOT mutate
    /// notification state.
    /// </summary>
    public const string Unknown = "unknown";
}
