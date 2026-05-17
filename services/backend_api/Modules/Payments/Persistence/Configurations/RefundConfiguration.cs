using BackendApi.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Payments.Persistence.Configurations;

public sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("refunds", "payments", t =>
        {
            t.HasCheckConstraint("CK_refunds_amount_positive",
                "\"Amount\" > 0");
            t.HasCheckConstraint("CK_refunds_state",
                @"""State"" IN ('pending','completed','failed')");
            t.HasCheckConstraint("CK_refunds_currency",
                "\"Currency\" IN ('SAR','EGP')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.PaymentId).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnType("text").IsRequired();
        builder.Property(x => x.Reason).HasColumnType("text");
        builder.Property(x => x.State).HasColumnType("text").IsRequired();
        builder.Property(x => x.ProviderRefundId).HasColumnType("text");
        builder.Property(x => x.InitiatedBy).IsRequired();
        builder.Property(x => x.FailedReason).HasColumnType("text");
        builder.Property(x => x.CompletedAt);
        builder.Property(x => x.FailedAt);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => x.PaymentId).HasDatabaseName("IX_refunds_payment");
        builder.HasIndex(x => new { x.PaymentId, x.State })
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_refunds_payment_state");
    }
}
