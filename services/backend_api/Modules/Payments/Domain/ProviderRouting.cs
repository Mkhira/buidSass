namespace BackendApi.Modules.Payments.Domain;

/// <summary>
/// Per-(market, method) primary + backup provider configuration per BR-2.
/// Constraint: <c>PrimaryProviderId != BackupProviderId</c> (V-6).
/// BR-13 — <see cref="AutoFailoverEnabled"/> default is <c>false</c>;
/// failover requires explicit operator action.
/// </summary>
public sealed class ProviderRouting
{
    public string MarketCode { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string PrimaryProviderId { get; set; } = string.Empty;
    public string? BackupProviderId { get; set; }
    public bool AutoFailoverEnabled { get; set; }
    public int FailoverThresholdPct { get; set; } = 50;
    public int FailoverWindowMinutes { get; set; } = 5;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
