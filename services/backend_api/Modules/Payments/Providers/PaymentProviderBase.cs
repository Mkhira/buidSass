using System.Text.Json;
using BackendApi.Modules.Payments.PciScope;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Payments.Providers;

/// <summary>
/// Shared scaffolding for the seven provider implementations: dispatch egress
/// guarded by <see cref="EgressPayloadFilter"/>, payload-key projection from
/// <see cref="CreatePaymentDispatch"/>, and a sandbox-style synthetic response
/// path. Real HTTP clients attach in a subsequent revision; the synthetic
/// path is sufficient to make the create-payment / refund / poll / webhook
/// paths exercisable end-to-end on Staging without burning provider quota.
/// </summary>
public abstract class PaymentProviderBase : IPaymentProvider
{
    protected readonly TimeProvider Clock;

    protected PaymentProviderBase(TimeProvider clock)
    {
        Clock = clock;
    }

    public abstract string ProviderId { get; }
    public abstract bool SupportsMarket(string marketCode);
    public abstract bool SupportsMethod(string method);
    public virtual bool SupportsLedgerApi => true;
    public virtual bool SupportsWebhookReplay => true;

    /// <summary>
    /// Indicates whether the provider's primary capture path is synchronous
    /// (returns <c>captured</c> on the create call — KSA card rails) versus
    /// asynchronous (returns a redirect URL or pending status — BNPL flows).
    /// </summary>
    protected virtual bool CaptureIsSynchronous => true;

    public virtual Task<CreatePaymentResult> CreatePaymentAsync(
        CreatePaymentDispatch dispatch, CancellationToken cancellationToken)
    {
        // BR-14 — guard the egress payload BEFORE the synthetic call.
        var payloadKeys = ProjectPayloadKeys(dispatch);
        EgressPayloadFilter.EnsureCreatePaymentPayloadAllowed(payloadKeys, ProviderId);

        var providerMessageId = $"{ProviderId.ToUpperInvariant()}-{dispatch.PaymentId:N}";
        if (CaptureIsSynchronous)
        {
            return Task.FromResult(new CreatePaymentResult(
                Success: true,
                CapturedSynchronously: true,
                ProviderMessageId: providerMessageId,
                HostedFieldsConfigJson: SerializeHostedFieldsConfig(dispatch),
                ExternalRedirectUrl: null,
                FailureReason: null,
                StatusCode: 200));
        }

        var redirectUrl = $"https://sandbox.{ProviderId}.example/checkout/{providerMessageId}";
        return Task.FromResult(new CreatePaymentResult(
            Success: true,
            CapturedSynchronously: false,
            ProviderMessageId: providerMessageId,
            HostedFieldsConfigJson: null,
            ExternalRedirectUrl: redirectUrl,
            FailureReason: null,
            StatusCode: 200));
    }

    public virtual Task<RefundResult> RefundAsync(RefundDispatch dispatch, CancellationToken cancellationToken)
    {
        // BR-14 — guard refund egress payload too.
        EgressPayloadFilter.EnsureRefundPayloadAllowed(new[]
        {
            "refund_id", "provider_message_id", "amount", "currency", "reason",
        }, ProviderId);

        var providerRefundId = $"{ProviderId.ToUpperInvariant()}-R-{dispatch.RefundId:N}";
        return Task.FromResult(new RefundResult(
            Success: true,
            ProviderRefundId: providerRefundId,
            FailureReason: null,
            StatusCode: 200));
    }

    public virtual Task<PaymentStatusResult> GetPaymentStatusAsync(
        string providerMessageId, CancellationToken cancellationToken)
    {
        return Task.FromResult(new PaymentStatusResult(
            CanonicalState: CanonicalWebhookEventKinds.Captured,
            Amount: null,
            OccurredAt: Clock.GetUtcNow(),
            RawPayloadRedactedJson: "{}"));
    }

    public abstract bool ValidateWebhookSignature(
        HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets);

    public abstract WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody);

    public virtual Task<SettlementLedger> FetchSettlementLedgerAsync(
        DateOnly date, CancellationToken cancellationToken)
    {
        // Sandbox-empty ledger; the reconciliation matcher correctly classifies
        // any internal `captured` row as `missing_on_provider` against this.
        return Task.FromResult(new SettlementLedger(
            ProviderId: ProviderId,
            Date: date,
            Rows: Array.Empty<SettlementLedgerRow>()));
    }

    public virtual Task<WebhookReplayResult> ReplayWebhooksAsync(
        DateRange range, CancellationToken cancellationToken)
    {
        if (!SupportsWebhookReplay)
        {
            return Task.FromResult(new WebhookReplayResult(
                Supported: false, RepliedEventsCount: 0,
                NotSupportedReason: $"Provider '{ProviderId}' does not expose a webhook replay endpoint at v1."));
        }
        return Task.FromResult(new WebhookReplayResult(
            Supported: true, RepliedEventsCount: 0, NotSupportedReason: null));
    }

    /// <summary>
    /// Returns the set of payload keys this provider will ship on a create-payment
    /// call. Used by the egress filter (BR-14) and by the egress-sweep test (T063).
    /// Providers that need extra fields override and add to this list, which
    /// triggers a CODEOWNERS-protected review of the allow-list change.
    /// </summary>
    protected virtual IEnumerable<string> ProjectPayloadKeys(CreatePaymentDispatch dispatch)
    {
        yield return "payment_id";
        yield return "order_id";
        yield return "market_code";
        yield return "method";
        yield return "amount";
        yield return "currency";
        yield return "idempotency_key";
        yield return "customer_ref";
        yield return "recipient_name_redacted";
        yield return "recipient_phone_masked_last4";
        yield return "merchant_ref";
        yield return "return_url";
        yield return "webhook_url";
    }

    /// <summary>
    /// Builds the hosted-fields config JSON returned to the client for
    /// synchronous-capture providers. The session_id is provider-issued in a
    /// real integration; this synthetic flavor uses the payment id.
    /// </summary>
    protected virtual string SerializeHostedFieldsConfig(CreatePaymentDispatch dispatch)
    {
        var config = new
        {
            provider = ProviderId,
            session_id = dispatch.PaymentId.ToString("N"),
            public_key = $"pk_sandbox_{ProviderId}",
            method = dispatch.Method,
            currency = dispatch.Currency,
        };
        return JsonSerializer.Serialize(config);
    }
}
