using BackendApi.Modules.Cms.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Cms.Persistence.Configurations;

public sealed class LegalPageVersionConfiguration : IEntityTypeConfiguration<LegalPageVersion>
{
    public void Configure(EntityTypeBuilder<LegalPageVersion> builder)
    {
        builder.ToTable("legal_page_versions", "cms", t =>
        {
            t.HasCheckConstraint("CK_cms_legal_kind",
                "\"LegalPageKind\" IN ('terms','privacy','returns','cookies')");
            t.HasCheckConstraint("CK_cms_legal_market_code",
                "\"MarketCode\" IN ('EG','KSA','*')");
            t.HasCheckConstraint("CK_cms_legal_state",
                "\"State\" IN ('draft','scheduled','live','superseded')");
            t.HasCheckConstraint("CK_cms_legal_supersede_consistency",
                "(\"State\" = 'superseded' AND \"SupersededAtUtc\" IS NOT NULL AND \"SupersededByVersionId\" IS NOT NULL) OR (\"State\" <> 'superseded' AND \"SupersededAtUtc\" IS NULL AND \"SupersededByVersionId\" IS NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.LegalPageKindWire).HasColumnName("LegalPageKind").HasColumnType("text").IsRequired();
        builder.Property(x => x.VersionLabel).HasColumnType("text").IsRequired();
        builder.Property(x => x.BodyAr).HasColumnType("text");
        builder.Property(x => x.BodyEn).HasColumnType("text");
        builder.Property(x => x.EffectiveAtUtc);
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.StateWire).HasColumnName("State").HasColumnType("text").IsRequired().HasDefaultValue("draft");
        builder.Property(x => x.SupersededAtUtc);
        builder.Property(x => x.SupersededByVersionId);
        builder.Property(x => x.VendorId);
        builder.Property(x => x.OwnerActorId).IsRequired();
        builder.Property(x => x.OwnershipOrphaned).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.LastStaleAlertAtUtc);
        builder.Property(x => x.LastStaleAlertDismissedAtUtc);
        builder.Property(x => x.CreatedAtUtc).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.EditorSaveAtUtc).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.PublishedAtUtc);
        builder.Property(x => x.Xmin).IsRowVersion().HasColumnName("xmin");

        builder.HasIndex(x => new { x.LegalPageKindWire, x.MarketCode, x.StateWire, x.EffectiveAtUtc })
            .HasDatabaseName("IX_cms_legal_history");
        builder.HasIndex(x => new { x.StateWire, x.EffectiveAtUtc })
            .HasDatabaseName("IX_cms_legal_worker_scan");
        // Partial unique index "exactly one live version per (kind, market)" added via raw SQL in the migration.
    }
}
