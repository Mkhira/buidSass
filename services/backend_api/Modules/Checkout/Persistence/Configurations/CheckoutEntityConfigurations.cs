using BackendApi.Modules.Checkout.Entities;
using BackendApi.Modules.Checkout.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Checkout.Persistence.Configurations;

public sealed class CheckoutSessionConfiguration : IEntityTypeConfiguration<CheckoutSession>
{
    public void Configure(EntityTypeBuilder<CheckoutSession> builder)
    {
        builder.ToTable("sessions", "checkout", t =>
        {
            t.HasCheckConstraint(
                "CK_checkout_sessions_state_enum",
                "\"State\" IN ('init','addressed','shipping_selected','payment_selected','submitted','confirmed','failed','expired')");
            t.HasCheckConstraint(
                "CK_checkout_sessions_identity_present",
                "\"AccountId\" IS NOT NULL OR \"CartTokenHash\" IS NOT NULL");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.State).HasColumnType("citext").IsRequired()
            .HasDefaultValue(CheckoutStates.Init);
        builder.Property(x => x.ShippingProviderId).HasColumnType("citext");
        builder.Property(x => x.ShippingMethodCode).HasColumnType("citext");
        builder.Property(x => x.PaymentMethod).HasColumnType("citext");
        builder.Property(x => x.CouponCode).HasColumnType("citext");
        builder.Property(x => x.FailureReasonCode).HasColumnType("citext");
        builder.Property(x => x.ShippingAddressJson).HasColumnType("jsonb");
        builder.Property(x => x.BillingAddressJson).HasColumnType("jsonb");
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.AccountId, x.State, x.LastTouchedAt })
            .HasDatabaseName("IX_checkout_sessions_account_state_touched");
        builder.HasIndex(x => new { x.State, x.ExpiresAt })
            .HasDatabaseName("IX_checkout_sessions_state_expires");
        builder.HasIndex(x => x.CartId);
        builder.HasIndex(x => x.MarketCode);
    }
}

public sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("payment_attempts", "checkout", t =>
        {
            t.HasCheckConstraint(
                "CK_checkout_payment_attempts_state_enum",
                "\"State\" IN ('initiated','authorized','captured','declined','voided','failed','refunded','pending_webhook')");
            t.HasCheckConstraint(
                "CK_checkout_payment_attempts_amount_non_negative",
                "\"AmountMinor\" >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderId).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Method).HasColumnType("citext").IsRequired();
        builder.Property(x => x.State).HasColumnType("citext").IsRequired()
            .HasDefaultValue(PaymentAttemptStates.Initiated);
        builder.Property(x => x.Currency).HasColumnType("citext").IsRequired();

        builder.HasIndex(x => new { x.SessionId, x.CreatedAt });
        builder.HasIndex(x => new { x.ProviderId, x.ProviderTxnId });
    }
}

public sealed class ShippingQuoteConfiguration : IEntityTypeConfiguration<ShippingQuote>
{
    public void Configure(EntityTypeBuilder<ShippingQuote> builder)
    {
        builder.ToTable("shipping_quotes", "checkout", t =>
        {
            t.HasCheckConstraint("CK_checkout_shipping_quotes_fee_non_negative", "\"FeeMinor\" >= 0");
            t.HasCheckConstraint("CK_checkout_shipping_quotes_eta_min_non_negative", "\"EtaMinDays\" >= 0");
            t.HasCheckConstraint("CK_checkout_shipping_quotes_eta_max_ge_min", "\"EtaMaxDays\" >= \"EtaMinDays\"");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderId).HasColumnType("citext").IsRequired();
        builder.Property(x => x.MethodCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Currency).HasColumnType("citext").IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.HasIndex(x => new { x.SessionId, x.ExpiresAt });
    }
}

public sealed class PaymentWebhookEventConfiguration : IEntityTypeConfiguration<PaymentWebhookEvent>
{
    public void Configure(EntityTypeBuilder<PaymentWebhookEvent> builder)
    {
        builder.ToTable("payment_webhook_events", "checkout");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderId).HasColumnType("citext").IsRequired();
        builder.Property(x => x.EventType).HasColumnType("citext").IsRequired();
        builder.Property(x => x.RawPayload).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        // Dedupe key per R7 — unique on (provider_id, provider_event_id).
        builder.HasIndex(x => new { x.ProviderId, x.ProviderEventId }).IsUnique();
        builder.HasIndex(x => x.ReceivedAt);
    }
}

public sealed class IdempotencyResultConfiguration : IEntityTypeConfiguration<IdempotencyResult>
{
    public void Configure(EntityTypeBuilder<IdempotencyResult> builder)
    {
        builder.ToTable("idempotency_results", "checkout");
        // Composite PK so the same client-supplied key cannot collide across accounts
        // (CR review PR #30 round 2). Submit requires auth (FR-019), so AccountId is always set.
        builder.HasKey(x => new { x.AccountId, x.IdempotencyKey });
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128);
        builder.Property(x => x.AccountId).IsRequired();
        builder.Property(x => x.ResponseJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.HasIndex(x => x.ExpiresAt);
    }
}
