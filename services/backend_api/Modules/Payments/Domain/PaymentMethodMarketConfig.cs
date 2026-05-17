namespace BackendApi.Modules.Payments.Domain;

/// <summary>
/// Per-market enabled methods + eligibility (V-7 + BR-9). Optional cart-total
/// bounds and a JSON eligibility bag drive admin-side customization without
/// schema changes.
/// </summary>
public sealed class PaymentMethodMarketConfig
{
    public string MarketCode { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public decimal? MinCartTotal { get; set; }
    public decimal? MaxCartTotal { get; set; }

    /// <summary>jsonb bag — e.g., COD postal-code allowlist.</summary>
    public string EligibilityJson { get; set; } = "{}";

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
