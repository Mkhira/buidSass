using BackendApi.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Notifications.Persistence.Configurations;

public sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("deliveries", "notifications", t =>
        {
            t.HasCheckConstraint("CK_deliveries_status",
                @"""Status"" IN ('accepted','delivered','bounced','failed','timeout','unregistered','soft_bounce')");
            t.HasCheckConstraint("CK_deliveries_attempt_positive",
                @"""AttemptNo"" > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.NotificationId).IsRequired();
        builder.Property(x => x.AttemptNo).IsRequired();
        builder.Property(x => x.ProviderId).HasColumnType("text").IsRequired();
        builder.Property(x => x.ProviderMessageId).HasColumnType("text");
        builder.Property(x => x.Status).HasColumnType("text").IsRequired();
        builder.Property(x => x.ErrorCode).HasColumnType("text");
        builder.Property(x => x.ErrorMessageRedacted).HasColumnType("text");
        builder.Property(x => x.RequestedAt).IsRequired();
        builder.Property(x => x.RespondedAt);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.NotificationId, x.AttemptNo })
            .IsUnique()
            .HasDatabaseName("UX_deliveries_notification_attempt");

        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("IX_deliveries_created_at");
    }
}
