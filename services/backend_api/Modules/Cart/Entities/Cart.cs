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

    // --- Shipping fee snapshot (spec 026 BR-3) ---
    // The shipping fee a customer is quoted at cart-creation is locked here.
    // Subsequent admin fee-table changes never retroactively alter an in-flight
    // cart. All three fields are nullable so existing carts created before
    // spec 026 shipped (and B2B / quote flows where shipping is computed
    // later) remain valid. The Shipping module owns the WRITER for these
    // fields; Cart is the storage owner only (read-only consumer per T013).
    /// <summary>Snapshot of the ShippingMethodVersion used to quote the fee.</summary>
    public Guid? ShippingMethodVersionIdSnapshot { get; set; }
    /// <summary>Fee amount at quote time. Currency is the cart's market currency.</summary>
    public decimal? ShippingFeeAmountSnapshot { get; set; }
    /// <summary>UTC timestamp at which the snapshot was locked. Used for BR-3 audit traces.</summary>
    public DateTimeOffset? ShippingFeeSnapshotAt { get; set; }
    /// <summary>
    /// Free-form JSON capturing the resolution context (zone id, weight kg,
    /// tier id, currency). Pure read-side metadata — never authoritative.
    /// </summary>
    public string? ShippingFeeSnapshotJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
