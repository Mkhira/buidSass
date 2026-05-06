namespace BackendApi.Modules.Support.Entities;

/// <summary>Per-market + per-priority SLA target row (FR-021).</summary>
public sealed class SlaPolicy
{
    public string MarketCode { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public int FirstResponseTargetMinutes { get; set; }
    public int ResolutionTargetMinutes { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Guid? UpdatedByActorId { get; set; }
}
