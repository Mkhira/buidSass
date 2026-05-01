using BackendApi.Modules.Cms.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Cms.Persistence.Configurations;

public sealed class CmsMarketSchemaConfiguration : IEntityTypeConfiguration<CmsMarketSchema>
{
    public void Configure(EntityTypeBuilder<CmsMarketSchema> builder)
    {
        builder.ToTable("market_schemas", "cms", t =>
        {
            t.HasCheckConstraint("CK_cms_market_code",
                "\"MarketCode\" IN ('EG','KSA','*')");
            t.HasCheckConstraint("CK_cms_banner_max_live",
                "\"BannerMaxLivePerSlot\" BETWEEN 1 AND 10");
            t.HasCheckConstraint("CK_cms_featured_max_refs",
                "\"FeaturedSectionMaxReferences\" BETWEEN 1 AND 100");
            t.HasCheckConstraint("CK_cms_preview_ttl",
                "\"PreviewTokenDefaultTtlHours\" BETWEEN 1 AND 168");
            t.HasCheckConstraint("CK_cms_stale_alert",
                "\"DraftStalenessAlertDays\" BETWEEN 7 AND 365");
            t.HasCheckConstraint("CK_cms_asset_grace",
                "\"AssetGracePeriodDays\" BETWEEN 0 AND 30");
        });

        builder.HasKey(x => x.MarketCode);

        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.BannerMaxLivePerSlot).IsRequired().HasDefaultValue(5);
        builder.Property(x => x.FeaturedSectionMaxReferences).IsRequired().HasDefaultValue(24);
        builder.Property(x => x.PreviewTokenDefaultTtlHours).IsRequired().HasDefaultValue(24);
        builder.Property(x => x.DraftStalenessAlertDays).IsRequired().HasDefaultValue(30);
        builder.Property(x => x.AssetGracePeriodDays).IsRequired().HasDefaultValue(7);
        builder.Property(x => x.LastEditedByActorId).IsRequired();
        builder.Property(x => x.LastEditedAtUtc).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.Xmin).IsRowVersion().HasColumnName("xmin");
    }
}
