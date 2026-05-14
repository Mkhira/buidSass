using BackendApi.Modules.Shipping.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Shipping.Persistence.Configurations;

public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("shipments", "shipping", t =>
        {
            t.HasCheckConstraint("CK_shipments_market_code",
                "\"MarketCode\" IN ('SA','EG')");
            t.HasCheckConstraint("CK_shipments_state",
                @"""State"" IN ('pending','label_purchased','handed_to_carrier','in_transit','out_for_delivery','delivered','delivery_attempted','return_to_sender_initiated','returned_to_sender','delivery_disputed','re_delivered_pending','closed_with_refund','failed_to_create_label','pending_label_provider_failure','dead_letter_label','label_voided')");
            t.HasCheckConstraint("CK_shipments_attempts_nonneg",
                "\"Attempts\" >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        // BR-4 — single-shipment-per-order enforced at DB level.
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.MethodVersionId).IsRequired();
        builder.Property(x => x.ProviderId).HasColumnType("text").IsRequired();
        builder.Property(x => x.ProviderTrackingId).HasColumnType("text");
        builder.Property(x => x.LabelPdfBlobUrl).HasColumnType("text");
        builder.Property(x => x.State).HasColumnType("text").IsRequired();
        builder.Property(x => x.ShipToAddressRedactedJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ParentShipmentId);
        builder.Property(x => x.Attempts).IsRequired();
        builder.Property(x => x.EtaMin);
        builder.Property(x => x.EtaMax);
        builder.Property(x => x.LabelPurchasedAt);
        builder.Property(x => x.HandedAt);
        builder.Property(x => x.InTransitAt);
        builder.Property(x => x.DeliveredAt);
        builder.Property(x => x.FailedReason).HasColumnType("text");
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);
        builder.Property(x => x.Xmin).IsRowVersion().HasColumnName("xmin");

        // Indexes from data-model.md §shipments.
        builder.HasIndex(x => x.OrderId)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("UX_shipments_order_id_active");
        builder.HasIndex(x => new { x.ProviderId, x.ProviderTrackingId })
            .HasDatabaseName("IX_shipments_provider_tracking");
        builder.HasIndex(x => x.ParentShipmentId)
            .HasDatabaseName("IX_shipments_parent");
        builder.HasIndex(x => new { x.State, x.MarketCode })
            .HasFilter("\"State\" IN ('pending','label_purchased','handed_to_carrier','in_transit','out_for_delivery','delivery_attempted','re_delivered_pending','pending_label_provider_failure')")
            .HasDatabaseName("IX_shipments_active_state");
    }
}
