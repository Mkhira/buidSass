using System.Text.Json;
using BackendApi.Modules.Payments.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Payments.Providers.Paymob;

/// <summary>Paymob — EG primary card provider; covers Apple Pay + Meeza rails (ADR-007 v1).</summary>
public sealed class PaymobProvider : PaymentProviderBase
{
    public PaymobProvider(TimeProvider clock) : base(clock) { }

    public override string ProviderId => PaymentsConstants.Providers.Paymob;

    public override bool SupportsMarket(string marketCode) =>
        marketCode == PaymentsConstants.Markets.EG;

    public override bool SupportsMethod(string method) => method switch
    {
        PaymentsConstants.Methods.Card => true,
        PaymentsConstants.Methods.ApplePay => true,
        PaymentsConstants.Methods.Meeza => true,
        _ => false,
    };

    public override bool ValidateWebhookSignature(
        HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets)
    {
        vaultSecrets.TryGetValue("payments/eg/paymob/webhook-signing-key", out var secret);
        return ProviderWebhookSignature.ValidateHexHmacSha256FromHeader(
            request, rawBody, "X-Paymob-Hmac", secret);
    }

    public override WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var msgId = root.TryGetProperty("transaction_id", out var id) ? id.GetString() ?? string.Empty : string.Empty;
        var success = root.TryGetProperty("success", out var s) && s.GetBoolean();
        var refunded = root.TryGetProperty("is_refund", out var r) && r.GetBoolean();
        var canonical = (refunded, success) switch
        {
            (true, _) => CanonicalWebhookEventKinds.Refunded,
            (_, true) => CanonicalWebhookEventKinds.Captured,
            _ => CanonicalWebhookEventKinds.Failed,
        };
        decimal? amount = null;
        if (root.TryGetProperty("amount_cents", out var a) && a.TryGetInt64(out var cents))
            amount = cents / 100m;
        return new WebhookEvent(msgId, success ? "success" : "failure", canonical, amount, Clock.GetUtcNow(), "{}");
    }
}
