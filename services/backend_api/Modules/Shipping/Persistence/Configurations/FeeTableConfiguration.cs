using BackendApi.Modules.Shipping.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Shipping.Persistence.Configurations;

public sealed class FeeTableConfiguration : IEntityTypeConfiguration<FeeTable>
{
    public void Configure(EntityTypeBuilder<FeeTable> builder)
    {
        builder.ToTable("fee_tables", "shipping", t =>
        {
            t.HasCheckConstraint("CK_fee_tables_weight_range",
                "\"WeightMinKg\" >= 0 AND \"WeightMaxKg\" > \"WeightMinKg\"");
            t.HasCheckConstraint("CK_fee_tables_fee_amount_nonneg",
                "\"FeeAmount\" >= 0");
            t.HasCheckConstraint("CK_fee_tables_currency",
                "\"Currency\" IN ('SAR','EGP')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.MethodVersionId).IsRequired();
        builder.Property(x => x.ZoneId).IsRequired();
        builder.Property(x => x.WeightMinKg).HasColumnType("numeric(6,2)").IsRequired();
        builder.Property(x => x.WeightMaxKg).HasColumnType("numeric(6,2)").IsRequired();
        builder.Property(x => x.FeeAmount).HasColumnType("numeric(10,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnType("text").IsRequired();
        builder.Property(x => x.EffectiveAt).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => new { x.MethodVersionId, x.ZoneId })
            .HasDatabaseName("IX_fee_tables_lookup");
    }
}
