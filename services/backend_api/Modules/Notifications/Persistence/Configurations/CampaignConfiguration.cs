using BackendApi.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Notifications.Persistence.Configurations;

public sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("campaigns", "notifications", t =>
        {
            t.HasCheckConstraint("CK_campaigns_state",
                @"""State"" IN ('draft','scheduled','sending','paused','completed','cancelled')");
            t.HasCheckConstraint("CK_campaigns_channel_not_otp",
                @"""Channel"" IN ('sms','email','push')");
            t.HasCheckConstraint("CK_campaigns_market_code",
                @"""MarketCode"" IN ('sa','eg')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.Name).HasColumnType("text").IsRequired();
        builder.Property(x => x.State).HasColumnType("text").IsRequired();
        builder.Property(x => x.TemplateId).IsRequired();
        builder.Property(x => x.TemplateVersionId);
        builder.Property(x => x.Channel).HasColumnType("text").IsRequired();
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.TargetCriteriaJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.SendAt);
        builder.Property(x => x.CreatedBy).IsRequired();
        builder.Property(x => x.RecipientCountSnapshot);
        builder.Property(x => x.StartedAt);
        builder.Property(x => x.CompletedAt);
        builder.Property(x => x.PausedAt);
        builder.Property(x => x.CancelledAt);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);
        builder.Property(x => x.Xmin).IsRowVersion().HasColumnName("xmin");

        builder.HasIndex(x => new { x.State, x.SendAt })
            .HasDatabaseName("IX_campaigns_state_send_at");

        builder.HasIndex(x => x.CreatedBy)
            .HasDatabaseName("IX_campaigns_created_by");

        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}
