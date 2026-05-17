using BackendApi.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Payments.Persistence.Configurations;

public sealed class ChargebackConfiguration : IEntityTypeConfiguration<Chargeback>
{
    public void Configure(EntityTypeBuilder<Chargeback> builder)
    {
        builder.ToTable("chargebacks", "payments", t =>
        {
            t.HasCheckConstraint("CK_chargebacks_status",
                @"""Status"" IN ('received','disputed','lost','won','accepted')");
            t.HasCheckConstraint("CK_chargebacks_amount_positive",
                "\"Amount\" > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.PaymentId).IsRequired();
        builder.Property(x => x.ProviderChargebackId).HasColumnType("text").IsRequired();
        builder.Property(x => x.Amount).HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.ReasonCode).HasColumnType("text");
        builder.Property(x => x.ReceivedAt).IsRequired();
        builder.Property(x => x.Status).HasColumnType("text").IsRequired();
        builder.Property(x => x.ResolutionNotes).HasColumnType("text");
        builder.Property(x => x.ResolvedAt);
        builder.Property(x => x.ResolvedBy);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => x.PaymentId).HasDatabaseName("IX_chargebacks_payment");
        builder.HasIndex(x => x.ProviderChargebackId)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("UX_chargebacks_provider_id");
    }
}
