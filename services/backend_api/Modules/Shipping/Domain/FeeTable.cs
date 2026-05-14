namespace BackendApi.Modules.Shipping.Domain;

/// <summary>
/// Per-method-version × zone fee tier per <c>data-model.md</c> §fee_tables.
/// The DB enforces a Postgres EXCLUDE constraint so two tiers can never
/// overlap on <c>(method_version_id, zone_id)</c> weight range simultaneously
/// (research §4 — race-proof). Application-level validators catch first.
/// </summary>
public sealed class FeeTable
{
    public Guid Id { get; set; }
    public Guid MethodVersionId { get; set; }
    public Guid ZoneId { get; set; }
    public decimal WeightMinKg { get; set; }
    public decimal WeightMaxKg { get; set; }
    public decimal FeeAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset EffectiveAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
