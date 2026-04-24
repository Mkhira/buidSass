namespace BackendApi.Modules.Cart.Entities;

public sealed class Cart
{
    public Guid Id { get; set; }
    public Guid? AccountId { get; set; }
    public byte[]? CartTokenHash { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    /// <summary>Status enum — see <see cref="BackendApi.Modules.Cart.Primitives.CartStatuses"/> for allowed values + transitions. The DB CHECK constraint enforces the enum at the schema level.</summary>
    public string Status { get; set; } = BackendApi.Modules.Cart.Primitives.CartStatuses.Active;
    public string? CouponCode { get; set; }
    public DateTimeOffset LastTouchedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ArchivedAt { get; set; }
    public string? ArchivedReason { get; set; }
    public uint RowVersion { get; set; }
    public string OwnerId { get; set; } = "platform";
    public Guid? VendorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
