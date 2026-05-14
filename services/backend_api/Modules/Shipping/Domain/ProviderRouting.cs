namespace BackendApi.Modules.Shipping.Domain;

/// <summary>
/// Per-<c>(market, method)</c> provider routing per <c>data-model.md</c>
/// §provider_routing. BR-1 — every market × method has a primary + optional
/// backup. BR-11 — auto-failover is opt-in (default <c>false</c> at v1).
/// </summary>
public sealed class ProviderRouting
{
    public string MarketCode { get; set; } = string.Empty;
    public Guid MethodId { get; set; }
    public string PrimaryProviderId { get; set; } = string.Empty;
    public string? BackupProviderId { get; set; }
    public bool AutoFailoverEnabled { get; set; }
    public int FailoverThresholdPct { get; set; } = 50;
    public int FailoverWindowMinutes { get; set; } = 5;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
