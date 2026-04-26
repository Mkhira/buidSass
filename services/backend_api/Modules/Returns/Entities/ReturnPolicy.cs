namespace BackendApi.Modules.Returns.Entities;

/// <summary>data-model.md table 8.</summary>
public sealed class ReturnPolicy
{
    public string MarketCode { get; set; } = string.Empty;
    public int ReturnWindowDays { get; set; }
    public int? AutoApproveUnderDays { get; set; }
    public int RestockingFeeBp { get; set; }
    public bool ShippingRefundOnFullOnly { get; set; } = true;
    public Guid? UpdatedByAccountId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
