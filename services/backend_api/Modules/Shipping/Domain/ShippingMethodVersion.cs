namespace BackendApi.Modules.Shipping.Domain;

/// <summary>
/// Per-version snapshot of a shipping method per <c>data-model.md</c>
/// §shipping_method_versions. Lifecycle states:
/// <c>draft → in_review → published → archived</c>.
/// Fee tables FK against this version so in-flight carts retain the rate
/// they were quoted at (BR-3).
/// </summary>
public sealed class ShippingMethodVersion
{
    public Guid Id { get; set; }
    public Guid MethodId { get; set; }
    public int VersionNo { get; set; }
    public string State { get; set; } = string.Empty;

    /// <summary>Free-form eligibility — min_cart_total, eligible_customer_tiers, time-window, etc.</summary>
    public string EligibilityJson { get; set; } = "{}";

    public int EtaMinHours { get; set; }
    public int EtaMaxHours { get; set; }

    public Guid AuthorId { get; set; }
    public Guid? ReviewerId { get; set; }

    /// <summary>If set, fee tables tied to this version do not apply before this time.</summary>
    public DateTimeOffset? EffectiveAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
