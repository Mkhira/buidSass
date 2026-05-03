using BackendApi.Modules.Pricing.Entities;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Pricing.Persistence;

public sealed class PricingDbContext(DbContextOptions<PricingDbContext> options) : DbContext(options)
{
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponRedemption> CouponRedemptions => Set<CouponRedemption>();
    public DbSet<B2BTier> B2BTiers => Set<B2BTier>();
    public DbSet<AccountB2BTier> AccountB2BTiers => Set<AccountB2BTier>();
    public DbSet<ProductTierPrice> ProductTierPrices => Set<ProductTierPrice>();
    public DbSet<PriceExplanation> PriceExplanations => Set<PriceExplanation>();
    public DbSet<BundleMembership> BundleMemberships => Set<BundleMembership>();

    // ---------- spec 007-b commercial-authoring tables ----------
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignLink> CampaignLinks => Set<CampaignLink>();
    public DbSet<PreviewProfile> PreviewProfiles => Set<PreviewProfile>();
    public DbSet<CommercialThreshold> CommercialThresholds => Set<CommercialThreshold>();
    public DbSet<CommercialApproval> CommercialApprovals => Set<CommercialApproval>();
    public DbSet<CommercialAuditEvent> CommercialAuditEvents => Set<CommercialAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("pricing");
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(PricingDbContext).Assembly,
            type => type.Namespace?.StartsWith("BackendApi.Modules.Pricing", StringComparison.Ordinal) == true);
    }
}
