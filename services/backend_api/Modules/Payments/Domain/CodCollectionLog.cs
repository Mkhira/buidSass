namespace BackendApi.Modules.Payments.Domain;

/// <summary>
/// Per-COD-Payment delivery + cash-receipt record per
/// <c>data-model.md §cod_collection_log</c>. Operator confirms cash receipt
/// at v1 (BR-9). The <see cref="CourierUserId"/> field is nullable until the
/// courier field-app integration ships in Phase 1.5.
/// </summary>
public sealed class CodCollectionLog
{
    public Guid PaymentId { get; set; }
    public Guid? CourierUserId { get; set; }
    public decimal? AmountCollected { get; set; }
    public DateTimeOffset? CollectedAt { get; set; }
    public DateTimeOffset? OperatorConfirmedAt { get; set; }
    public Guid? OperatorId { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
