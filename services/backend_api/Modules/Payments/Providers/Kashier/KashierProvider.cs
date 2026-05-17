using System.Text.Json;
using BackendApi.Modules.Payments.Primitives;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Payments.Providers.Kashier;

/// <summary>
/// Kashier — EG cards backup (ADR-007 v1). Settlement ledger is delivered via
/// SFTP-CSV at v1 (research §4); <see cref="SupportsLedgerApi"/> is therefore
/// <c>false</c> so the daily reconciliation job routes to the CSV fetcher.
/// </summary>
public sealed class KashierProvider : PaymentProviderBase
{
    public KashierProvider(TimeProvider clock) : base(clock) { }

    public override string ProviderId => PaymentsConstants.Providers.Kashier;

    public override bool SupportsMarket(string marketCode) =>
        marketCode == PaymentsConstants.Markets.EG;

    public override bool SupportsMethod(string method) => method switch
    {
        PaymentsConstants.Methods.Card => true,
        _ => false,
    };

    public override bool SupportsLedgerApi => false;
    // Per research §8 — Kashier supports replay via dashboard export only.
    public override bool SupportsWebhookReplay => true;

    public override bool ValidateWebhookSignature(
        HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets)
    {
        vaultSecrets.TryGetValue("payments/eg/kashier/webhook-signing-key", out var secret);
        return ProviderWebhookSignature.ValidateHexHmacSha256FromHeader(
            request, rawBody, "X-Kashier-Signature", secret);
    }

    public override WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        // Use Kashier's per-transaction id (transactionId / paymentId) for the
        // provider-message-id so retries / new attempts for the same order
        // don't collide. merchantOrderId is order-scoped and unsafe for
        // BR-4 idempotency.
        var msgId = root.TryGetProperty("transactionId", out var tid) ? tid.GetString() ?? string.Empty
            : root.TryGetProperty("paymentId", out var pid) ? pid.GetString() ?? string.Empty
            : string.Empty;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "FAILED" : "FAILED";
        var canonical = status switch
        {
            "SUCCESS" => CanonicalWebhookEventKinds.Captured,
            "REFUND" => CanonicalWebhookEventKinds.Refunded,
            "FAILED" or "DECLINED" => CanonicalWebhookEventKinds.Failed,
            _ => CanonicalWebhookEventKinds.Unknown,
        };
        decimal? amount = null;
        if (root.TryGetProperty("amount", out var a) && a.TryGetDecimal(out var dec)) amount = dec;
        return new WebhookEvent(msgId, status, canonical, amount, Clock.GetUtcNow(), "{}");
    }
}
