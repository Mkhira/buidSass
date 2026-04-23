namespace BackendApi.Modules.Pricing.Entities;

public sealed class CouponRedemption
{
    public Guid Id { get; set; }
    public Guid CouponId { get; set; }
    public Guid AccountId { get; set; }
    public Guid? OrderId { get; set; }
    public DateTimeOffset RedeemedAt { get; set; }
}
