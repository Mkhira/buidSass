namespace BackendApi.Modules.Checkout.Entities;

public sealed class CheckoutSession
{
    public Guid Id { get; set; }
    public Guid CartId { get; set; }
    public Guid? AccountId { get; set; }
    public byte[]? CartTokenHash { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    /// <summary>Enum — see <see cref="BackendApi.Modules.Checkout.Primitives.CheckoutStates"/>.</summary>
    public string State { get; set; } = BackendApi.Modules.Checkout.Primitives.CheckoutStates.Init;

    // Addresses captured as immutable JSON snapshots so edits to the account's address book
    // after submit don't rewrite historical checkout/order state (Principle 25 auditability).
    public string? ShippingAddressJson { get; set; }
    public string? BillingAddressJson { get; set; }

    public string? ShippingProviderId { get; set; }
    public string? ShippingMethodCode { get; set; }
    public long? ShippingFeeMinor { get; set; }

    public string? PaymentMethod { get; set; }
    public string? CouponCode { get; set; }

    public Guid? IssuedExplanationId { get; set; }
    public byte[]? LastPreviewHash { get; set; }
    public DateTimeOffset? AcceptedDriftAt { get; set; }

    public DateTimeOffset LastTouchedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public DateTimeOffset? ExpiredAt { get; set; }

    public Guid? OrderId { get; set; }
    public string? FailureReasonCode { get; set; }

    public uint RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
