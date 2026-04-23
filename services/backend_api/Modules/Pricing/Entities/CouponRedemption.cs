namespace BackendApi.Modules.Pricing.Entities;

public sealed class CouponRedemption
{
    public Guid Id { get; set; }
    public Guid CouponId { get; set; }
    public Guid AccountId { get; set; }
    public Guid? OrderId { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public DateTimeOffset RedeemedAt { get; set; }
}
