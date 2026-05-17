namespace BackendApi.Modules.Notifications.Domain;

/// <summary>
/// Per-market × channel provider selection per
/// <c>data-model.md §notifications.provider_routing</c>. Composite PK
/// <c>(market_code, channel)</c>. <see cref="AutoFailoverEnabled"/> defaults
/// to <c>false</c> at v1 (clarify-locked) — auto-failover is opt-in per row.
/// </summary>
public sealed class ProviderRouting
{
    public string MarketCode { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;

    public string PrimaryProviderId { get; set; } = string.Empty;
    public string? BackupProviderId { get; set; }

    public bool AutoFailoverEnabled { get; set; }
    public int FailoverThresholdPct { get; set; } = 50;
    public int FailoverWindowMinutes { get; set; } = 5;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
