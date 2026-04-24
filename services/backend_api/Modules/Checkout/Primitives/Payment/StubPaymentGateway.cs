namespace BackendApi.Modules.Checkout.Primitives.Payment;

/// <summary>
/// Dev/test gateway. Always succeeds for supported methods; deterministic provider-txn ids
/// derive from the session id so integration tests can assert on them. Real providers replace
/// this via ADR-007.
///
/// Bank transfer + COD methods are NOT supported here — they never call a real gateway;
/// Submit handles them by creating the attempt row in `initiated` / `pending_webhook` state
/// directly and skipping the gateway round-trip.
/// </summary>
public sealed class StubPaymentGateway : IPaymentGateway
{
    public string ProviderId => "stub";

    private static readonly HashSet<string> SupportedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "card", "mada", "apple_pay", "stc_pay", "bnpl",
    };

    public bool Supports(string marketCode, string paymentMethod) =>
        SupportedMethods.Contains(paymentMethod);

    public Task<AuthorizeOutcome> AuthorizeAsync(AuthorizeRequest request, CancellationToken ct)
    {
        // Deterministic provider txn id per (session, method) so idempotent retries in tests
        // see the same value without needing a clock-dependent id.
        var seed = $"{ProviderId}:{request.SessionId:N}:{request.Method}";
        var txnId = DeterministicGuid(seed).ToString("N");
        return Task.FromResult(new AuthorizeOutcome(
            IsSuccess: true,
            ProviderTxnId: txnId,
            Kind: AuthorizeResultKind.CapturedSynchronously));
    }

    public Task<CaptureOutcome> CaptureAsync(Guid providerTxnId, long amountMinor, CancellationToken ct)
        => Task.FromResult(new CaptureOutcome(true));

    public Task<VoidOutcome> VoidAsync(Guid providerTxnId, string reason, CancellationToken ct)
        => Task.FromResult(new VoidOutcome(true));

    public Task<RefundOutcome> RefundAsync(Guid providerTxnId, long amountMinor, string reason, CancellationToken ct)
        => Task.FromResult(new RefundOutcome(true));

    public Task<WebhookTranslation?> HandleWebhookAsync(WebhookEnvelope envelope, CancellationToken ct)
    {
        // Stub just echoes what the caller sent — real providers decode provider-specific JSON.
        return Task.FromResult<WebhookTranslation?>(new WebhookTranslation(
            ProviderTxnId: envelope.ProviderEventId,
            MappedAttemptState: PaymentAttemptStates.Captured,
            ErrorCode: null,
            ErrorMessage: null));
    }

    private static Guid DeterministicGuid(string seed)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        return new Guid(guidBytes);
    }
}
