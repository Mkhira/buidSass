using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Payments.Providers;

/// <summary>
/// Provider abstraction per <c>contracts/payments-contract.md §6</c>. Every
/// provider impl MUST be config-replaceable (Principle 13). Business logic
/// MUST NOT branch on <c>ProviderId</c> outside this folder.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Canonical provider identifier (matches <c>PaymentsConstants.Providers</c>).</summary>
    string ProviderId { get; }

    /// <summary>Returns <c>true</c> if the provider supports the given market.</summary>
    bool SupportsMarket(string marketCode);

    /// <summary>Returns <c>true</c> if the provider supports the given payment method.</summary>
    bool SupportsMethod(string method);

    /// <summary>True if the provider supports settlement-ledger API (false = SFTP-CSV fallback).</summary>
    bool SupportsLedgerApi { get; }

    /// <summary>True if the provider's events-API supports webhook replay (BR-16).</summary>
    bool SupportsWebhookReplay { get; }

    /// <summary>Creates a payment intent / authorization with the provider. Idempotent on dispatch.IdempotencyKey.</summary>
    Task<CreatePaymentResult> CreatePaymentAsync(CreatePaymentDispatch dispatch, CancellationToken cancellationToken);

    /// <summary>Initiates a refund (full or partial) against a previously-captured payment.</summary>
    Task<RefundResult> RefundAsync(RefundDispatch dispatch, CancellationToken cancellationToken);

    /// <summary>Polls the provider for the current payment status (fallback when webhooks are slow).</summary>
    Task<PaymentStatusResult> GetPaymentStatusAsync(string providerMessageId, CancellationToken cancellationToken);

    /// <summary>Validates inbound webhook HMAC signature against KV-stored secret. Fail-closed (V-3).</summary>
    bool ValidateWebhookSignature(HttpRequest request, byte[] rawBody, IReadOnlyDictionary<string, string> vaultSecrets);

    /// <summary>Parses provider-specific webhook payload into the canonical event shape.</summary>
    WebhookEvent ParseWebhookEvent(HttpRequest request, byte[] rawBody);

    /// <summary>Fetches the provider's settlement ledger for the given date. Used by daily reconciliation.</summary>
    Task<SettlementLedger> FetchSettlementLedgerAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>Replays webhooks across the given range. Returns <c>NotSupported</c> if the provider does not expose the API.</summary>
    Task<WebhookReplayResult> ReplayWebhooksAsync(DateRange range, CancellationToken cancellationToken);
}

/// <summary>
/// Payload sent to <see cref="IPaymentProvider.CreatePaymentAsync"/>. PII is
/// minimized per BR-14: recipient name redacted to first + last initial, phone
/// masked to last-4. The <see cref="EgressPayloadFilter"/> rejects any payload
/// that contains fields outside the BR-14 allow-list.
/// </summary>
public sealed record CreatePaymentDispatch(
    Guid PaymentId,
    Guid OrderId,
    string MarketCode,
    string Method,
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    /// <summary>Customer reference (UUID/hash — NEVER an email or PII).</summary>
    string CustomerRef,
    string RecipientNameRedacted,
    string RecipientPhoneMaskedLast4);

public sealed record CreatePaymentResult(
    bool Success,
    /// <summary>Set when the provider returns a synchronous capture (KSA cards).</summary>
    bool CapturedSynchronously,
    string? ProviderMessageId,
    /// <summary>Hosted-fields config JSON (for synchronous flows) or null.</summary>
    string? HostedFieldsConfigJson,
    /// <summary>External redirect URL (for BNPL / external flows) or null.</summary>
    string? ExternalRedirectUrl,
    string? FailureReason,
    /// <summary>HTTP-style status code from the provider (for retry-policy classification).</summary>
    int? StatusCode);

public sealed record RefundDispatch(
    Guid RefundId,
    string ProviderMessageId,
    decimal Amount,
    string Currency,
    string? Reason);

public sealed record RefundResult(
    bool Success,
    string? ProviderRefundId,
    string? FailureReason,
    int? StatusCode);

public sealed record PaymentStatusResult(
    string CanonicalState,
    decimal? Amount,
    DateTimeOffset? OccurredAt,
    string? RawPayloadRedactedJson);

/// <summary>Canonical webhook event used by all providers; mapped onto <see cref="StateMachines.PaymentStateMachine"/>.</summary>
public sealed record WebhookEvent(
    string ProviderMessageId,
    string EventKind,
    string CanonicalEventKind,
    decimal? Amount,
    DateTimeOffset OccurredAt,
    string RawPayloadRedactedJson);

/// <summary>Canonical event kinds emitted by provider-specific parsers.</summary>
public static class CanonicalWebhookEventKinds
{
    public const string Authorized = "authorized";
    public const string Captured = "captured";
    public const string Failed = "failed";
    public const string Refunded = "refunded";
    public const string Chargeback = "chargeback";
    public const string Expired = "expired";
    /// <summary>
    /// Reserved for events the parser recognized as well-formed but cannot
    /// map onto a state transition (e.g. provider-internal notifications,
    /// new event kinds added after our adapter shipped). The webhook handler
    /// MUST record the receipt for audit + idempotency but MUST NOT mutate
    /// payment state.
    /// </summary>
    public const string Unknown = "unknown";
}

/// <summary>Provider settlement ledger snapshot returned by <see cref="IPaymentProvider.FetchSettlementLedgerAsync"/>.</summary>
public sealed record SettlementLedger(
    string ProviderId,
    DateOnly Date,
    IReadOnlyList<SettlementLedgerRow> Rows);

/// <summary>
/// One row of a provider settlement ledger consumed by the daily
/// reconciliation matcher. BR-14 / SAQ-A: providers MUST redact every
/// cardholder field BEFORE constructing this record. The optional
/// <see cref="RawRedactedRowJson"/> blob is for operator-facing exception
/// drill-down — it MUST never carry PAN, CVV, expiry, or any other
/// cardholder data; the egress sweep (T067) and PciScopeMonitor cover
/// shape-drift detection on the storage side, but each provider's adapter
/// is the first line of defense and is responsible for stripping these
/// fields at the source.
/// </summary>
public sealed record SettlementLedgerRow(
    string ProviderMessageId,
    decimal Amount,
    string Currency,
    DateTimeOffset SettledAt,
    string? RawRedactedRowJson);

public sealed record DateRange(DateTimeOffset From, DateTimeOffset To);

public sealed record WebhookReplayResult(
    bool Supported,
    int RepliedEventsCount,
    string? NotSupportedReason);
