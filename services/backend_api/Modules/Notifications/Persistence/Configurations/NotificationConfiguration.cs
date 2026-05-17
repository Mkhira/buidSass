using BackendApi.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Notifications.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", "notifications", t =>
        {
            t.HasCheckConstraint("CK_notifications_market_code",
                @"""MarketCode"" IN ('sa','eg')");
            t.HasCheckConstraint("CK_notifications_locale",
                @"""Locale"" IN ('ar','en')");
            t.HasCheckConstraint("CK_notifications_channel",
                @"""Channel"" IN ('sms','email','push')");
            t.HasCheckConstraint("CK_notifications_recipient_kind",
                @"""RecipientKind"" IN ('customer','admin','anonymous')");
            t.HasCheckConstraint("CK_notifications_state",
                @"""State"" IN ('pending','queued','sending','delivered','failed','retrying','dead_letter','skipped')");
            t.HasCheckConstraint("CK_notifications_idempotency_key_sha256",
                @"""IdempotencyKey"" ~ '^[0-9a-fA-F]{64}$'");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.CorrelationId).IsRequired();
        builder.Property(x => x.RecipientId);
        builder.Property(x => x.RecipientKind).HasColumnType("text").IsRequired();
        builder.Property(x => x.Channel).HasColumnType("text").IsRequired();
        builder.Property(x => x.EventKind).HasColumnType("text").IsRequired();
        builder.Property(x => x.TemplateVersionId);
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.Locale).HasColumnType("text").IsRequired();
        builder.Property(x => x.State).HasColumnType("text").IsRequired();
        builder.Property(x => x.SkippedReason).HasColumnType("text");
        builder.Property(x => x.FailedReason).HasColumnType("text");
        builder.Property(x => x.ProviderId).HasColumnType("text");
        builder.Property(x => x.ProviderMessageId).HasColumnType("text");
        builder.Property(x => x.Attempts).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnType("text").IsRequired();
        builder.Property(x => x.PayloadRedactedJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CampaignId);
        builder.Property(x => x.NotBefore);
        builder.Property(x => x.DeliveredAt);
        builder.Property(x => x.FailedAt);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);
        builder.Property(x => x.Xmin).IsRowVersion().HasColumnName("xmin");

        builder.HasIndex(x => new { x.State, x.Channel })
            .HasFilter(@"""State"" IN ('pending','queued','retrying')")
            .HasDatabaseName("IX_notifications_active_work");

        builder.HasIndex(x => x.IdempotencyKey)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("UX_notifications_idempotency_key_active");

        builder.HasIndex(x => new { x.RecipientId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_notifications_recipient_created_desc");

        builder.HasIndex(x => new { x.CampaignId, x.State })
            .HasDatabaseName("IX_notifications_campaign_state");

        builder.HasIndex(x => new { x.ProviderId, x.ProviderMessageId })
            .HasDatabaseName("IX_notifications_provider_message");

        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}
