using BackendApi.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Payments.Persistence.Configurations;

public sealed class PaymentMethodMarketConfigConfiguration : IEntityTypeConfiguration<PaymentMethodMarketConfig>
{
    public void Configure(EntityTypeBuilder<PaymentMethodMarketConfig> builder)
    {
        builder.ToTable("payment_methods_market_config", "payments", t =>
        {
            t.HasCheckConstraint("CK_payment_methods_market",
                "\"MarketCode\" IN ('sa','eg')");
            t.HasCheckConstraint("CK_payment_methods_cart_total_bounds",
                "\"MinCartTotal\" IS NULL OR \"MaxCartTotal\" IS NULL OR \"MinCartTotal\" <= \"MaxCartTotal\"");
            t.HasCheckConstraint("CK_payment_methods_min_cart_nonneg",
                "\"MinCartTotal\" IS NULL OR \"MinCartTotal\" >= 0");
            t.HasCheckConstraint("CK_payment_methods_max_cart_nonneg",
                "\"MaxCartTotal\" IS NULL OR \"MaxCartTotal\" >= 0");
        });

        builder.HasKey(x => new { x.MarketCode, x.Method });
        builder.Property(x => x.MarketCode).HasColumnType("text");
        builder.Property(x => x.Method).HasColumnType("text");
        builder.Property(x => x.Enabled).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.MinCartTotal).HasColumnType("numeric(12,2)");
        builder.Property(x => x.MaxCartTotal).HasColumnType("numeric(12,2)");
        builder.Property(x => x.EligibilityJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
    }
}
