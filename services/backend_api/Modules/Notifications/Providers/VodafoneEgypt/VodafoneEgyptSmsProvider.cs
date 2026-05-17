using System.Text.Json;
using BackendApi.Modules.Notifications.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Notifications.Providers.VodafoneEgypt;

/// <summary>
/// Vodafone Egypt — primary EG SMS provider. Webhooks deliver DLRs HMAC-SHA256
/// signed in <c>X-VFEG-Signature</c>. ADR-009 v1 stack.
/// </summary>
public sealed class VodafoneEgyptSmsProvider : INotificationProvider
{
    private readonly TimeProvider _clock;

    public VodafoneEgyptSmsProvider(TimeProvider clock) { _clock = clock; }

    public string ProviderId => NotificationsConstants.Providers.VodafoneEgypt;
    public string Channel => NotificationsConstants.Channels.Sms;
    public bool SupportsMarket(string marketCode) => marketCode == NotificationsConstants.Markets.Eg;

    public Task<SendResult> SendAsync(NotificationDispatch dispatch, CancellationToken cancellationToken)
    {
        var msgId = $"vfeg-sandbox-{dispatch.IdempotencyKey[..16]}";
        return Task.FromResult(SendResult.Ok(msgId));
    }

    public bool ValidateWebhookSignature(HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets)
    {
        vaultSecrets.TryGetValue("notifications-sms/eg/vodafone-egypt/webhook-signing-key", out var secret);
        return NotificationWebhookSignature.ValidateHexHmacSha256FromHeader(
            request, rawBody, "X-VFEG-Signature", secret);
    }

    public WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var msgId = root.TryGetProperty("messageId", out var mid) ? mid.GetString() ?? string.Empty : string.Empty;
        var dlr = root.TryGetProperty("status", out var st) ? st.GetString() ?? "unknown" : "unknown";
        var canonical = dlr.ToLowerInvariant() switch
        {
            "delivrd" or "delivered" => CanonicalWebhookEventKinds.Delivered,
            "enroute" or "accepted" => CanonicalWebhookEventKinds.Accepted,
            "expired" or "deleted" or "undeliv" => CanonicalWebhookEventKinds.Bounced,
            "rejectd" or "failed" => CanonicalWebhookEventKinds.Failed,
            _ => CanonicalWebhookEventKinds.Unknown,
        };
        string? errorCode = root.TryGetProperty("errCode", out var ec) ? ec.GetString() : null;
        return new WebhookEvent(ProviderId, msgId, dlr, canonical, _clock.GetUtcNow(), errorCode);
    }
}
