using BackendApi.Modules.Shipping.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Shipping.Persistence.Configurations;

public sealed class ShippingMethodConfiguration : IEntityTypeConfiguration<ShippingMethod>
{
    public void Configure(EntityTypeBuilder<ShippingMethod> builder)
    {
        builder.ToTable("shipping_methods", "shipping", t =>
        {
            t.HasCheckConstraint("CK_shipping_methods_market_code",
                "\"MarketCode\" IN ('SA','EG')");
            t.HasCheckConstraint("CK_shipping_methods_name_lengths",
                "char_length(\"NameAr\") BETWEEN 1 AND 120 AND char_length(\"NameEn\") BETWEEN 1 AND 120");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.NameAr).HasColumnType("text").IsRequired();
        builder.Property(x => x.NameEn).HasColumnType("text").IsRequired();
        builder.Property(x => x.DescriptionAr).HasColumnType("text");
        builder.Property(x => x.DescriptionEn).HasColumnType("text");
        builder.Property(x => x.CurrentVersionId);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => new { x.MarketCode, x.DeletedAt })
            .HasDatabaseName("IX_shipping_methods_market");
    }
}
