using BackendApi.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Payments.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", "payments", t =>
        {
            t.HasCheckConstraint("CK_payments_market_code",
                "\"MarketCode\" IN ('sa','eg')");
            t.HasCheckConstraint("CK_payments_currency",
                "\"Currency\" IN ('SAR','EGP')");
            t.HasCheckConstraint("CK_payments_amount_positive",
                "\"Amount\" > 0");
            t.HasCheckConstraint("CK_payments_method",
                @"""Method"" IN ('card','apple_pay','mada','stc_pay','meeza','bnpl_tabby','bnpl_tamara','bnpl_valu','cod','bank_transfer')");
            t.HasCheckConstraint("CK_payments_state",
                @"""State"" IN ('pending_authorization','pending_external_redirect','pending_collection_on_delivery','pending_bank_transfer','authorized','capture_failed','captured','failed','expired','refunded','partially_refunded','chargeback_received')");
            t.HasCheckConstraint("CK_payments_currency_matches_market",
                @"(""MarketCode"" = 'sa' AND ""Currency"" = 'SAR') OR (""MarketCode"" = 'eg' AND ""Currency"" = 'EGP')");
            // V-1 (defense-in-depth at the schema layer) — no cardholder-shaped column names allowed.
            // Real enforcement is in the CI guard and PciScopeMonitor; this check is a tripwire only.
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.Method).HasColumnType("text").IsRequired();
        builder.Property(x => x.ProviderId).HasColumnType("text");
        builder.Property(x => x.ProviderMessageId).HasColumnType("text");
        builder.Property(x => x.State).HasColumnType("text").IsRequired();
        builder.Property(x => x.Amount).HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnType("text").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnType("text").IsRequired();
        builder.Property(x => x.AttemptId).IsRequired();
        builder.Property(x => x.FailedReason).HasColumnType("text");
        builder.Property(x => x.ExpiredReason).HasColumnType("text");
        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.RecipientPayloadRedactedJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.AuthorizedAt);
        builder.Property(x => x.CapturedAt);
        builder.Property(x => x.FailedAt);
        builder.Property(x => x.ExpiredAt);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);
        builder.Property(x => x.Xmin).IsRowVersion().HasColumnName("xmin");

        // Indexes from data-model.md §payments.
        builder.HasIndex(x => new { x.OrderId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_payments_order_created_desc");

        builder.HasIndex(x => x.State)
            .HasFilter(@"""State"" IN ('pending_authorization','pending_external_redirect','pending_collection_on_delivery','pending_bank_transfer','authorized','capture_failed')")
            .HasDatabaseName("IX_payments_active_state");

        builder.HasIndex(x => new { x.ProviderId, x.ProviderMessageId })
            .HasDatabaseName("IX_payments_provider_message");

        builder.HasIndex(x => x.IdempotencyKey)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("UX_payments_idempotency_key_active");

        builder.HasIndex(x => x.CustomerId)
            .HasDatabaseName("IX_payments_customer");
    }
}
