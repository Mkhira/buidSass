namespace BackendApi.Modules.Orders.Entities;

/// <summary>
/// Per-market cancellation-policy row. Read by <see cref="Primitives.CancellationPolicy"/>
/// at request time; admin-editable (FR-022). Seeded for KSA + EG by Phase B B3.
/// </summary>
public sealed class CancellationPolicyRow
{
    public string MarketCode { get; set; } = string.Empty;
    public bool AuthorizedCancelAllowed { get; set; } = true;
    public int CapturedCancelHours { get; set; } = 24;
    public Guid? UpdatedByAccountId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
