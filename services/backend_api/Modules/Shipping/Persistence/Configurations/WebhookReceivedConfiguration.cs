using BackendApi.Modules.Shipping.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Shipping.Persistence.Configurations;

public sealed class WebhookReceivedConfiguration : IEntityTypeConfiguration<WebhookReceived>
{
    public void Configure(EntityTypeBuilder<WebhookReceived> builder)
    {
        builder.ToTable("webhooks_received", "shipping");
        // Composite PK per BR-6 — idempotent on (provider_id, tracking_id, event_kind, occurred_at).
        builder.HasKey(x => new { x.ProviderId, x.ProviderTrackingId, x.EventKind, x.OccurredAt });
        builder.Property(x => x.ProviderId).HasColumnType("text").IsRequired();
        builder.Property(x => x.ProviderTrackingId).HasColumnType("text").IsRequired();
        builder.Property(x => x.EventKind).HasColumnType("text").IsRequired();
        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.SignatureValidated).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
    }
}
