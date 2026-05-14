using BackendApi.Modules.Shipping.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Shipping.Persistence.Configurations;

public sealed class DeadLetterLabelConfiguration : IEntityTypeConfiguration<DeadLetterLabel>
{
    public void Configure(EntityTypeBuilder<DeadLetterLabel> builder)
    {
        builder.ToTable("dead_letter_labels", "shipping", t =>
        {
            t.HasCheckConstraint("CK_dead_letter_resolution",
                "\"Resolution\" IS NULL OR \"Resolution\" IN ('retry','discard','manual_label')");
            t.HasCheckConstraint("CK_dead_letter_resolved_consistency",
                "(\"ResolvedAt\" IS NULL AND \"Resolution\" IS NULL) OR (\"ResolvedAt\" IS NOT NULL AND \"Resolution\" IS NOT NULL)");
        });
        builder.HasKey(x => x.ShipmentId);
        builder.Property(x => x.ShipmentId).IsRequired();
        builder.Property(x => x.LastErrorMessageRedacted).HasColumnType("text");
        builder.Property(x => x.LastErrorCode).HasColumnType("text");
        builder.Property(x => x.EnteredAt).IsRequired();
        builder.Property(x => x.ResolvedAt);
        builder.Property(x => x.Resolution).HasColumnType("text");
        builder.Property(x => x.ResolvedBy);
    }
}
