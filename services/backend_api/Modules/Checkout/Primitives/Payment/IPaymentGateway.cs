namespace BackendApi.Modules.Checkout.Primitives.Payment;

/// <summary>
/// Payment gateway abstraction (FR-005, Principle 13). Real providers (Mada, STC Pay, card
/// processor, BNPL, …) implement this interface; spec 010 ships only a StubPaymentGateway
/// that always succeeds in Dev/Test — ADR-007 picks the launch provider later in the phase.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The provider's stable identifier (e.g. `stub`, `hyperpay`, `checkoutcom`).</summary>
    string ProviderId { get; }

    /// <summary>True when this provider supports the given market + payment method combo.</summary>
    bool Supports(string marketCode, string paymentMethod);

    Task<AuthorizeOutcome> AuthorizeAsync(AuthorizeRequest request, CancellationToken ct);
    Task<CaptureOutcome> CaptureAsync(Guid providerTxnId, long amountMinor, CancellationToken ct);
    Task<VoidOutcome> VoidAsync(Guid providerTxnId, string reason, CancellationToken ct);
    Task<RefundOutcome> RefundAsync(Guid providerTxnId, long amountMinor, string reason, CancellationToken ct);

    /// <summary>
    /// Verify + interpret an inbound webhook. Returning a non-null result means the handler
    /// should persist the translated event + update the linked attempt; returning null means
    /// the payload didn't parse (deduped / unknown / signature mismatch) and the handler
    /// should still emit 2xx so the provider stops retrying (R7).
    /// </summary>
    Task<WebhookTranslation?> HandleWebhookAsync(WebhookEnvelope envelope, CancellationToken ct);
}

public sealed record AuthorizeRequest(
    Guid SessionId,
    string Method,
    long AmountMinor,
    string Currency,
    string? ProviderToken,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record AuthorizeOutcome(
    bool IsSuccess,
    string ProviderTxnId,
    AuthorizeResultKind Kind,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public enum AuthorizeResultKind
{
    Authorized,          // funds held, ready to capture
    CapturedSynchronously, // provider auto-captures (e.g. immediate debit)
    PendingWebhook,      // async — webhook will finalize (e.g. 3DS redirect)
    Declined,
    Failed,
}

public sealed record CaptureOutcome(bool IsSuccess, string? ErrorCode = null, string? ErrorMessage = null);
public sealed record VoidOutcome(bool IsSuccess, string? ErrorCode = null, string? ErrorMessage = null);
public sealed record RefundOutcome(bool IsSuccess, string? ErrorCode = null, string? ErrorMessage = null);

public sealed record WebhookEnvelope(
    string ProviderId,
    string Signature,
    string EventType,
    string ProviderEventId,
    string RawPayload);

public sealed record WebhookTranslation(
    string ProviderTxnId,
    string MappedAttemptState,
    string? ErrorCode,
    string? ErrorMessage);
