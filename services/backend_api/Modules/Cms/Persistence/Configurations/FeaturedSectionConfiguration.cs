using BackendApi.Modules.Cms.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Cms.Persistence.Configurations;

public sealed class FeaturedSectionConfiguration : IEntityTypeConfiguration<FeaturedSection>
{
    public void Configure(EntityTypeBuilder<FeaturedSection> builder)
    {
        builder.ToTable("featured_sections", "cms", t =>
        {
            t.HasCheckConstraint("CK_cms_featured_section_kind",
                "\"SectionKind\" IN ('home_top','home_mid','category_landing','b2b_landing')");
            t.HasCheckConstraint("CK_cms_featured_market_code",
                "\"MarketCode\" IN ('EG','KSA','*')");
            t.HasCheckConstraint("CK_cms_featured_state",
                "\"State\" IN ('draft','scheduled','live','archived')");
            t.HasCheckConstraint("CK_cms_featured_refs_size",
                "jsonb_array_length(\"References\") BETWEEN 0 AND 100");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.SectionKindWire).HasColumnName("SectionKind").HasColumnType("text").IsRequired();
        builder.Property(x => x.TitleAr).HasColumnType("text");
        builder.Property(x => x.TitleEn).HasColumnType("text");
        builder.Property(x => x.SubtitleAr).HasColumnType("text");
        builder.Property(x => x.SubtitleEn).HasColumnType("text");
        builder.Property(x => x.ReferencesJson)
            .HasColumnName("References")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();
        builder.Property(x => x.DisplayPriority).IsRequired().HasDefaultValue(100);
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.StateWire).HasColumnName("State").HasColumnType("text").IsRequired().HasDefaultValue("draft");
        builder.Property(x => x.ScheduledPublishAtUtc);
        builder.Property(x => x.VendorId);
        builder.Property(x => x.OwnerActorId).IsRequired();
        builder.Property(x => x.OwnershipOrphaned).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.LastStaleAlertAtUtc);
        builder.Property(x => x.LastStaleAlertDismissedAtUtc);
        builder.Property(x => x.LastPartialBrokenAlertAtUtc);
        builder.Property(x => x.CreatedAtUtc).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.EditorSaveAtUtc).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.PublishedAtUtc);
        builder.Property(x => x.ArchivedAtUtc);
        builder.Property(x => x.ArchiveReasonNote).HasColumnType("text");
        builder.Property(x => x.Xmin).IsRowVersion().HasColumnName("xmin");

        builder.HasIndex(x => new { x.StateWire, x.MarketCode, x.SectionKindWire, x.DisplayPriority, x.CreatedAtUtc })
            .HasDatabaseName("IX_cms_featured_storefront_read");
        builder.HasIndex(x => new { x.StateWire, x.ScheduledPublishAtUtc })
            .HasDatabaseName("IX_cms_featured_worker_scan");
        // Polymorphic GIN index on References created via raw SQL in the migration.
    }
}
