using BackendApi.Modules.Cms.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Cms.Persistence.Configurations;

public sealed class CmsAssetConfiguration : IEntityTypeConfiguration<CmsAsset>
{
    public void Configure(EntityTypeBuilder<CmsAsset> builder)
    {
        builder.ToTable("assets", "cms", t =>
        {
            t.HasCheckConstraint("CK_cms_asset_intended_locale",
                "\"IntendedLocale\" IS NULL OR \"IntendedLocale\" IN ('ar','en','*')");
            t.HasCheckConstraint("CK_cms_asset_storage_state",
                "\"StorageObjectState\" IN ('active','swept')");
            t.HasCheckConstraint("CK_cms_asset_size_positive",
                "\"SizeBytes\" >= 0");
            t.HasCheckConstraint("CK_cms_asset_market_code",
                "\"MarketCode\" IN ('EG','KSA','*')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.StorageObjectId).IsRequired();
        builder.Property(x => x.Mime).HasColumnType("text").IsRequired();
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.IntendedLocale).HasColumnType("text");
        builder.Property(x => x.OriginalFilename).HasColumnType("text").IsRequired();
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired().HasDefaultValue("*");
        builder.Property(x => x.StorageObjectStateWire).HasColumnName("StorageObjectState").HasColumnType("text").IsRequired().HasDefaultValue("active");
        builder.Property(x => x.DereferencedAtUtc);
        builder.Property(x => x.SweptAtUtc);
        builder.Property(x => x.UploadedByActorId).IsRequired();
        builder.Property(x => x.UploadedAtUtc).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.Xmin).IsRowVersion().HasColumnName("xmin");

        builder.HasIndex(x => new { x.StorageObjectStateWire, x.DereferencedAtUtc })
            .HasDatabaseName("IX_cms_assets_gc_scan");
    }
}
