using BackendApi.Modules.Shipping.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Shipping.Persistence.Configurations;

public sealed class ShipmentEventConfiguration : IEntityTypeConfiguration<ShipmentEvent>
{
    public void Configure(EntityTypeBuilder<ShipmentEvent> builder)
    {
        builder.ToTable("shipment_events", "shipping");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ShipmentId).IsRequired();
        builder.Property(x => x.ProviderEventKind).HasColumnType("text").IsRequired();
        builder.Property(x => x.InternalStateAtEvent).HasColumnType("text").IsRequired();
        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.ReceivedAt).IsRequired();
        builder.Property(x => x.RawPayloadRedactedJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.ShipmentId, x.OccurredAt })
            .HasDatabaseName("IX_shipment_events_shipment_occurred");
    }
}
