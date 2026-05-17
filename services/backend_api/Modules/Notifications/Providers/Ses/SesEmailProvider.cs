using System.Text.Json;
using BackendApi.Modules.Notifications.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Notifications.Providers.Ses;

/// <summary>
/// SES — primary email provider, both markets. Bounce/complaint/delivery
/// events arrive via SNS topic subscriptions. ADR-009 v1 stack.
///
/// <para><strong>Sandbox-only signature validation.</strong></para>
/// Real SNS message verification requires fetching the X.509 certificate at
/// <c>SigningCertURL</c>, validating its hostname (<c>sns.&lt;region&gt;.amazonaws.com</c>
/// + signature-version 1/2 canonical-string assembly), and verifying the
/// payload's RSA/SHA-1 (SigVer 1) or RSA/SHA-256 (SigVer 2) signature against
/// the cert public key. That implementation lands when T011 KV creds are
/// populated and the real AWS SDK is wired into <c>SendAsync</c>.
/// <para>
/// Until then, this validator only accepts payloads carrying a vault-shared
/// HMAC-SHA256 in <c>X-SNS-HMAC</c>. The previous "envelope contains
/// SigningCertURL → accept" heuristic was removed because it accepted any
/// well-formed JSON without verifying any signature (CodeRabbit pass-1
/// Critical: that would have been a production hole if the dispatch worker
/// was wired to a public webhook ingress before the real validator landed).
/// </para>
/// </summary>
public sealed class SesEmailProvider : INotificationProvider
{
    private readonly TimeProvider _clock;

    public SesEmailProvider(TimeProvider clock) { _clock = clock; }

    public string ProviderId => NotificationsConstants.Providers.Ses;
    public string Channel => NotificationsConstants.Channels.Email;
    public bool SupportsMarket(string marketCode) => NotificationsConstants.Markets.All.Contains(marketCode);

    public Task<SendResult> SendAsync(NotificationDispatch dispatch, CancellationToken cancellationToken)
    {
        // Sandbox impl — real wiring will use AWSSDK.SimpleEmail SendEmailAsync.
        // Returns a deterministic synthetic MessageId so downstream
        // (DispatchWorker, audit, idempotency) flows are exercisable end-to-end
        // without a live AWS account.
        var msgId = $"ses-sandbox-{dispatch.IdempotencyKey[..16]}";
        return Task.FromResult(SendResult.Ok(msgId));
    }

    public bool ValidateWebhookSignature(HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets)
    {
        // Sandbox: HMAC-SHA256 against a vault-shared key. Real SNS cert
        // verification lands with the production SES wiring (see class doc).
        vaultSecrets.TryGetValue("notifications-email/multi/ses/webhook-signing-key", out var secret);
        return NotificationWebhookSignature.ValidateHexHmacSha256FromHeader(
            request, rawBody, "X-SNS-HMAC", secret);
    }

    public WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var msgId = root.TryGetProperty("MessageId", out var mid) ? mid.GetString() ?? string.Empty : string.Empty;
        var notificationType = root.TryGetProperty("notificationType", out var nt) ? nt.GetString() ?? "unknown" : "unknown";
        var canonical = notificationType.ToLowerInvariant() switch
        {
            "delivery" => CanonicalWebhookEventKinds.Delivered,
            "bounce" => CanonicalWebhookEventKinds.Bounced,
            "complaint" => CanonicalWebhookEventKinds.Complaint,
            _ => CanonicalWebhookEventKinds.Unknown,
        };
        string? errorCode = null;
        if (root.TryGetProperty("bounce", out var bounce)
            && bounce.TryGetProperty("bounceType", out var bt))
        {
            errorCode = bt.GetString();
        }
        return new WebhookEvent(ProviderId, msgId, notificationType, canonical, _clock.GetUtcNow(), errorCode);
    }
}
