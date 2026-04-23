using BackendApi.Modules.Pricing.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Pricing.Persistence.Configurations;

public sealed class TaxRateConfiguration : IEntityTypeConfiguration<TaxRate>
{
    public void Configure(EntityTypeBuilder<TaxRate> builder)
    {
        builder.ToTable("tax_rates", "pricing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Kind).HasColumnType("citext").IsRequired();
        builder.Property(x => x.RateBps).IsRequired();
        builder.Property(x => x.EffectiveFrom).IsRequired();
        builder.HasIndex(x => new { x.MarketCode, x.Kind, x.EffectiveFrom }).IsUnique();
    }
}

public sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        builder.ToTable("promotions", "pricing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Name).HasColumnType("text").IsRequired();
        builder.Property(x => x.ConfigJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.AppliesToProductIds).HasColumnType("uuid[]");
        builder.Property(x => x.AppliesToCategoryIds).HasColumnType("uuid[]");
        builder.Property(x => x.MarketCodes).HasColumnType("citext[]").IsRequired();
        builder.Property(x => x.Priority).IsRequired();
        builder.Property(x => x.OwnerId).HasColumnType("citext");
        builder.Property(x => x.IsActive).IsRequired();
        builder.HasIndex(x => new { x.IsActive, x.DeletedAt });
        builder.HasIndex(x => x.Priority);
    }
}

public sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("coupons", "pricing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasColumnType("citext").IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Kind).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Value).IsRequired();
        builder.Property(x => x.MarketCodes).HasColumnType("citext[]").IsRequired();
        builder.Property(x => x.OwnerId).HasColumnType("citext");
        // Optimistic concurrency is enforced by the unique index on coupon_redemptions
        // (coupon_id, account_id, order_id) + per-customer-limit check.
    }
}

public sealed class CouponRedemptionConfiguration : IEntityTypeConfiguration<CouponRedemption>
{
    public void Configure(EntityTypeBuilder<CouponRedemption> builder)
    {
        builder.ToTable("coupon_redemptions", "pricing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.HasIndex(x => new { x.CouponId, x.AccountId });
        // Postgres treats NULL as distinct in UNIQUE constraints → split into two partial indexes.
        builder.HasIndex(x => new { x.CouponId, x.AccountId, x.OrderId })
            .IsUnique()
            .HasFilter("\"OrderId\" IS NOT NULL")
            .HasDatabaseName("IX_coupon_redemptions_coupon_account_order_not_null");
        builder.HasIndex(x => new { x.CouponId, x.AccountId })
            .IsUnique()
            .HasFilter("\"OrderId\" IS NULL")
            .HasDatabaseName("IX_coupon_redemptions_coupon_account_order_null");
    }
}

public sealed class B2BTierConfiguration : IEntityTypeConfiguration<B2BTier>
{
    public void Configure(EntityTypeBuilder<B2BTier> builder)
    {
        builder.ToTable("b2b_tiers", "pricing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Slug).HasColumnType("citext").IsRequired();
        builder.HasIndex(x => x.Slug).IsUnique();
        builder.Property(x => x.Name).HasColumnType("text").IsRequired();
    }
}

public sealed class AccountB2BTierConfiguration : IEntityTypeConfiguration<AccountB2BTier>
{
    public void Configure(EntityTypeBuilder<AccountB2BTier> builder)
    {
        builder.ToTable("account_b2b_tiers", "pricing");
        builder.HasKey(x => x.AccountId);
    }
}

public sealed class ProductTierPriceConfiguration : IEntityTypeConfiguration<ProductTierPrice>
{
    public void Configure(EntityTypeBuilder<ProductTierPrice> builder)
    {
        builder.ToTable("product_tier_prices", "pricing");
        builder.HasKey(x => new { x.ProductId, x.TierId, x.MarketCode });
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.NetMinor).IsRequired();
    }
}

public sealed class PriceExplanationConfiguration : IEntityTypeConfiguration<PriceExplanation>
{
    public void Configure(EntityTypeBuilder<PriceExplanation> builder)
    {
        builder.ToTable("price_explanations", "pricing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OwnerKind).HasColumnType("citext").IsRequired();
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.ExplanationJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ExplanationHash).HasColumnType("bytea").IsRequired();
        builder.HasIndex(x => new { x.OwnerKind, x.OwnerId })
            .IsUnique()
            .HasFilter("\"OwnerKind\" IN ('quote','order')");
    }
}

public sealed class BundleMembershipConfiguration : IEntityTypeConfiguration<BundleMembership>
{
    public void Configure(EntityTypeBuilder<BundleMembership> builder)
    {
        builder.ToTable("bundle_memberships", "pricing");
        builder.HasKey(x => new { x.BundleProductId, x.ComponentProductId });
    }
}
