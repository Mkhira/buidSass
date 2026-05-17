using BackendApi.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Payments.Persistence.Configurations;

public sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("idempotency_keys", "payments", t =>
        {
            // BR-3 — keys are sha256(order_id:method:attempt_id) → 64 hex chars.
            t.HasCheckConstraint("CK_idempotency_keys_sha256",
                "\"Key\" ~ '^[0-9a-fA-F]{64}$'");
        });

        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasColumnType("text");
        builder.Property(x => x.PaymentId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.ExpiresAt);

        builder.HasIndex(x => x.PaymentId).HasDatabaseName("IX_idempotency_keys_payment");
        builder.HasIndex(x => x.ExpiresAt).HasDatabaseName("IX_idempotency_keys_expires");
    }
}
