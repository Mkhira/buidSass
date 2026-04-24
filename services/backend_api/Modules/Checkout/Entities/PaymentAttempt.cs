namespace BackendApi.Modules.Checkout.Entities;

public sealed class PaymentAttempt
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = string.Empty;
    /// <summary>Enum — see <see cref="BackendApi.Modules.Checkout.Primitives.PaymentAttemptStates"/>.</summary>
    public string State { get; set; } = BackendApi.Modules.Checkout.Primitives.PaymentAttemptStates.Initiated;
    public string? ProviderTxnId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
