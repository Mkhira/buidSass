using BackendApi.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Notifications.Persistence.Configurations;

public sealed class CampaignRecipientConfiguration : IEntityTypeConfiguration<CampaignRecipient>
{
    public void Configure(EntityTypeBuilder<CampaignRecipient> builder)
    {
        builder.ToTable("campaign_recipients", "notifications", t =>
        {
            t.HasCheckConstraint("CK_campaign_recipients_skipped_reason",
                @"""SkippedReason"" IS NULL OR ""SkippedReason"" IN ('channel_disabled_by_customer','rate_limited','recipient_deactivated','quiet_hours','opted_out')");
        });

        builder.HasKey(x => new { x.CampaignId, x.RecipientId });
        builder.Property(x => x.NotificationId);
        builder.Property(x => x.SkippedReason).HasColumnType("text");
        builder.Property(x => x.MaterializedAt).IsRequired();

        builder.HasIndex(x => x.NotificationId)
            .HasDatabaseName("IX_campaign_recipients_notification");
    }
}
