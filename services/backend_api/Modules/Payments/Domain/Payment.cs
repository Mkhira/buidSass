namespace BackendApi.Modules.Payments.Domain;

/// <summary>
/// Per-attempt payment record per <c>data-model.md §payments.payments</c>.
/// BR-1 — NO cardholder data is stored. PAN, CVV, expiry, track-data are
/// NEVER present on this entity; only the provider's token-equivalent is
/// referenced via <see cref="ProviderMessageId"/>. BR-6 — failed payments
/// are immutable; retry creates a fresh row referencing the same
/// <see cref="OrderId"/>. The order's payment_status reflects the latest
/// attempt (resolved by <c>ORDER BY created_at DESC LIMIT 1</c>).
/// </summary>
public sealed class Payment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;

    /// <summary>Resolved at attempt creation; nullable for COD / bank_transfer (native, no provider).</summary>
    public string? ProviderId { get; set; }

    /// <summary>Populated by provider response; nullable while pending.</summary>
    public string? ProviderMessageId { get; set; }

    public string State { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    /// <summary>BR-3 idempotency key: unique per (order_id, method, attempt_id).</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Client-generated UUID supporting BR-3 idempotency.</summary>
    public Guid AttemptId { get; set; }

    public string? FailedReason { get; set; }
    public string? ExpiredReason { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>Redacted recipient payload per BR-14 (PII minimization at egress).</summary>
    public string RecipientPayloadRedactedJson { get; set; } = "{}";

    public DateTimeOffset? AuthorizedAt { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public DateTimeOffset? ExpiredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>EF Core RowVersion mapping (xmin) per project pattern.</summary>
    public uint Xmin { get; set; }
}
