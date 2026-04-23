namespace BackendApi.Modules.Pricing.Entities;

// Reserved for analytics — no runtime use in v1. Admin inspection only.
public sealed class BundleMembership
{
    public Guid BundleProductId { get; set; }
    public Guid ComponentProductId { get; set; }
    public int Qty { get; set; }
}
