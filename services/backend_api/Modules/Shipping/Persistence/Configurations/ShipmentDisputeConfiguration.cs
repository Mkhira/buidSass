using BackendApi.Modules.Shipping.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Shipping.Persistence.Configurations;

public sealed class ShipmentDisputeConfiguration : IEntityTypeConfiguration<ShipmentDispute>
{
    public void Configure(EntityTypeBuilder<ShipmentDispute> builder)
    {
        builder.ToTable("shipment_disputes", "shipping", t =>
        {
            t.HasCheckConstraint("CK_shipment_disputes_reported_by",
                "\"ReportedBy\" IN ('customer','support_agent')");
            t.HasCheckConstraint("CK_shipment_disputes_status",
                "\"Status\" IN ('open','re_delivered','closed_with_refund','closed_no_action')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ShipmentId).IsRequired();
        builder.Property(x => x.ReportedAt).IsRequired();
        builder.Property(x => x.ReportedBy).HasColumnType("text").IsRequired();
        builder.Property(x => x.Status).HasColumnType("text").IsRequired();
        builder.Property(x => x.ResolutionNotes).HasColumnType("text");
        builder.Property(x => x.ResolvedAt);
        builder.Property(x => x.ResolvedBy);

        builder.HasIndex(x => new { x.ShipmentId, x.Status })
            .HasDatabaseName("IX_shipment_disputes_shipment_status");
    }
}
