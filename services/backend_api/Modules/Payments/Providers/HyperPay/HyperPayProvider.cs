using System.Text.Json;
using BackendApi.Modules.Payments.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Payments.Providers.HyperPay;

/// <summary>
/// HyperPay — KSA primary card provider (Visa/MC + Mada) plus Apple Pay +
/// STC Pay rails. ADR-007 v1 stack. Synchronous capture path for KSA cards.
/// </summary>
public sealed class HyperPayProvider : PaymentProviderBase
{
    public HyperPayProvider(TimeProvider clock) : base(clock) { }

    public override string ProviderId => PaymentsConstants.Providers.HyperPay;

    public override bool SupportsMarket(string marketCode) =>
        marketCode == PaymentsConstants.Markets.SA;

    public override bool SupportsMethod(string method) => method switch
    {
        PaymentsConstants.Methods.Card => true,
        PaymentsConstants.Methods.Mada => true,
        PaymentsConstants.Methods.ApplePay => true,
        PaymentsConstants.Methods.StcPay => true,
        _ => false,
    };

    public override bool ValidateWebhookSignature(
        HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets)
    {
        vaultSecrets.TryGetValue("payments/sa/hyperpay/webhook-signing-key", out var secret);
        return ProviderWebhookSignature.ValidateHexHmacSha256FromHeader(
            request, rawBody, "X-HyperPay-Signature", secret);
    }

    public override WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
    {
        // Synthetic parser — real impl will use a typed Refit response.
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var msgId = root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty;
        var kind = root.TryGetProperty("event", out var ev) ? ev.GetString() ?? "unknown" : "unknown";
        var canonical = kind switch
        {
            "payment.captured" or "payment.success" => CanonicalWebhookEventKinds.Captured,
            "payment.authorized" => CanonicalWebhookEventKinds.Authorized,
            "payment.failed" or "payment.declined" => CanonicalWebhookEventKinds.Failed,
            "payment.refunded" => CanonicalWebhookEventKinds.Refunded,
            "chargeback.received" => CanonicalWebhookEventKinds.Chargeback,
            _ => CanonicalWebhookEventKinds.Failed,
        };
        decimal? amount = null;
        if (root.TryGetProperty("amount", out var amt) && amt.TryGetDecimal(out var a)) amount = a;
        return new WebhookEvent(msgId, kind, canonical, amount, Clock.GetUtcNow(), "{}");
    }
}
