using BackendApi.Modules.Shipping.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Shipping.Persistence.Configurations;

public sealed class ShippingMethodVersionConfiguration
    : IEntityTypeConfiguration<ShippingMethodVersion>
{
    public void Configure(EntityTypeBuilder<ShippingMethodVersion> builder)
    {
        builder.ToTable("shipping_method_versions", "shipping", t =>
        {
            t.HasCheckConstraint("CK_method_versions_state",
                "\"State\" IN ('draft','in_review','published','archived')");
            t.HasCheckConstraint("CK_method_versions_version_no",
                "\"VersionNo\" >= 1");
            t.HasCheckConstraint("CK_method_versions_eta",
                "\"EtaMinHours\" >= 0 AND \"EtaMaxHours\" >= \"EtaMinHours\"");
            // V-1 publish gate (DB-level): once a version has published, ReviewerId
            // MUST be set AND distinct from AuthorId. The earlier
            // (PublishedAt IS NULL OR ReviewerId IS NULL OR ReviewerId <> AuthorId)
            // form let a published row with ReviewerId NULL slip through — that
            // would break AC-15.
            t.HasCheckConstraint("CK_method_versions_reviewer_not_author",
                "\"PublishedAt\" IS NULL OR (\"ReviewerId\" IS NOT NULL AND \"ReviewerId\" <> \"AuthorId\")");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.MethodId).IsRequired();
        builder.Property(x => x.VersionNo).IsRequired();
        builder.Property(x => x.State).HasColumnType("text").IsRequired();
        builder.Property(x => x.EligibilityJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.EtaMinHours).IsRequired();
        builder.Property(x => x.EtaMaxHours).IsRequired();
        builder.Property(x => x.AuthorId).IsRequired();
        builder.Property(x => x.ReviewerId);
        builder.Property(x => x.EffectiveAt);
        builder.Property(x => x.PublishedAt);
        builder.Property(x => x.ArchivedAt);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => new { x.MethodId, x.VersionNo })
            .IsUnique()
            .HasDatabaseName("UX_method_versions_method_version_no");
        builder.HasIndex(x => new { x.MethodId, x.State })
            .HasDatabaseName("IX_method_versions_state");
    }
}
