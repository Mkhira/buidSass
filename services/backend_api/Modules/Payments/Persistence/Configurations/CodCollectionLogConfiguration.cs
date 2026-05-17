using BackendApi.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Payments.Persistence.Configurations;

public sealed class CodCollectionLogConfiguration : IEntityTypeConfiguration<CodCollectionLog>
{
    public void Configure(EntityTypeBuilder<CodCollectionLog> builder)
    {
        builder.ToTable("cod_collection_log", "payments", t =>
        {
            t.HasCheckConstraint("CK_cod_collection_log_outcome",
                @"""Outcome"" IN ('collected','refused','address_not_found')");
            t.HasCheckConstraint("CK_cod_collection_log_amount_nonneg",
                "\"AmountCollected\" IS NULL OR \"AmountCollected\" >= 0");
        });

        builder.HasKey(x => x.PaymentId);
        builder.Property(x => x.PaymentId);
        builder.Property(x => x.CourierUserId);
        builder.Property(x => x.AmountCollected).HasColumnType("numeric(12,2)");
        builder.Property(x => x.CollectedAt);
        builder.Property(x => x.OperatorConfirmedAt);
        builder.Property(x => x.OperatorId);
        builder.Property(x => x.Outcome).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);
    }
}
