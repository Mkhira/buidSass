namespace BackendApi.Modules.Pricing.Entities;

public sealed class Coupon
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Kind { get; set; } = "percent";   // percent | amount
    public int Value { get; set; }                   // percent: bps; amount: minor units
    public long? CapMinor { get; set; }
    public int? PerCustomerLimit { get; set; }
    public int? OverallLimit { get; set; }
    public int UsedCount { get; set; }
    public bool ExcludesRestricted { get; set; }
    public string[] MarketCodes { get; set; } = Array.Empty<string>();
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public string? OwnerId { get; set; }
    public Guid? VendorId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
