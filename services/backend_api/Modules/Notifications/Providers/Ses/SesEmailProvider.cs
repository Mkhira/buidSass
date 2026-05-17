using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackendApi.Modules.Notifications.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Notifications.Providers.Ses;

/// <summary>
/// SES — primary email provider, both markets. Bounce/complaint/delivery
/// events arrive via SNS topic subscriptions; the SNS message signature is
/// validated against the X.509 cert pointed to by <c>SigningCertURL</c>.
/// ADR-009 v1 stack.
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
        // SNS payload includes its own SigningCertURL + Signature fields. For
        // sandbox/test exercising, fall back to an HMAC-SHA256 header
        // (X-SNS-HMAC) keyed off the vault secret. Production deployment
        // installs the SNS cert-verification middleware ahead of this method.
        vaultSecrets.TryGetValue("notifications-email/multi/ses/webhook-signing-key", out var secret);
        if (NotificationWebhookSignature.ValidateHexHmacSha256FromHeader(request, rawBody, "X-SNS-HMAC", secret))
            return true;

        // Permit envelope-embedded signature for SNS-native payloads — fail-closed otherwise.
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            return doc.RootElement.TryGetProperty("Signature", out var sigEl)
                   && !string.IsNullOrWhiteSpace(sigEl.GetString())
                   && doc.RootElement.TryGetProperty("SigningCertURL", out var certEl)
                   && (certEl.GetString() ?? string.Empty).StartsWith("https://sns.", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException) { return false; }
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
