using BackendApi.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Notifications.Persistence.Configurations;

public sealed class WebhookReceivedConfiguration : IEntityTypeConfiguration<WebhookReceived>
{
    public void Configure(EntityTypeBuilder<WebhookReceived> builder)
    {
        builder.ToTable("webhooks_received", "notifications");

        builder.HasKey(x => new { x.ProviderId, x.ProviderMessageId, x.EventKind });
        builder.Property(x => x.ProviderId).HasColumnType("text");
        builder.Property(x => x.ProviderMessageId).HasColumnType("text");
        builder.Property(x => x.EventKind).HasColumnType("text");
        builder.Property(x => x.ReceivedAt).IsRequired();
        builder.Property(x => x.SignatureValidated).IsRequired();

        builder.HasIndex(x => x.ReceivedAt)
            .HasDatabaseName("IX_webhooks_received_at");
    }
}
