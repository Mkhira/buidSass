using BackendApi.Modules.Cms.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Cms.Persistence.Configurations;

public sealed class BannerSlotConfiguration : IEntityTypeConfiguration<BannerSlot>
{
    public void Configure(EntityTypeBuilder<BannerSlot> builder)
    {
        builder.ToTable("banner_slots", "cms", t =>
        {
            t.HasCheckConstraint("CK_cms_banners_slot_kind",
                "\"SlotKind\" IN ('hero_top','category_strip','footer_strip','home_secondary')");
            t.HasCheckConstraint("CK_cms_banners_cta_kind",
                "\"CtaKind\" IN ('link','category','product','bundle','external_url','none')");
            t.HasCheckConstraint("CK_cms_banners_cta_health",
                "\"CtaHealth\" IN ('verified','broken','transient_unverified','not_applicable')");
            t.HasCheckConstraint("CK_cms_banners_market_code",
                "\"MarketCode\" IN ('EG','KSA','*')");
            t.HasCheckConstraint("CK_cms_banners_state",
                "\"State\" IN ('draft','scheduled','live','archived')");
            t.HasCheckConstraint("CK_cms_banners_headline_ar_len",
                "\"HeadlineAr\" IS NULL OR char_length(\"HeadlineAr\") <= 120");
            t.HasCheckConstraint("CK_cms_banners_headline_en_len",
                "\"HeadlineEn\" IS NULL OR char_length(\"HeadlineEn\") <= 120");
            t.HasCheckConstraint("CK_cms_banners_subhead_ar_len",
                "\"SubheadAr\" IS NULL OR char_length(\"SubheadAr\") <= 240");
            t.HasCheckConstraint("CK_cms_banners_subhead_en_len",
                "\"SubheadEn\" IS NULL OR char_length(\"SubheadEn\") <= 240");
            t.HasCheckConstraint("CK_cms_banners_schedule_window",
                "\"ScheduledStartUtc\" IS NULL OR \"ScheduledEndUtc\" IS NULL OR \"ScheduledEndUtc\" > \"ScheduledStartUtc\"");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.SlotKindWire).HasColumnName("SlotKind").HasColumnType("text").IsRequired();
        builder.Property(x => x.HeadlineAr).HasColumnType("text");
        builder.Property(x => x.HeadlineEn).HasColumnType("text");
        builder.Property(x => x.SubheadAr).HasColumnType("text");
        builder.Property(x => x.SubheadEn).HasColumnType("text");
        builder.Property(x => x.AssetIdAr);
        builder.Property(x => x.AssetIdEn);
        builder.Property(x => x.CtaKindWire).HasColumnName("CtaKind").HasColumnType("text").IsRequired();
        builder.Property(x => x.CtaTarget).HasColumnType("text");
        builder.Property(x => x.CtaHealthWire).HasColumnName("CtaHealth").HasColumnType("text").IsRequired().HasDefaultValue("not_applicable");
        builder.Property(x => x.ScheduledStartUtc);
        builder.Property(x => x.ScheduledEndUtc);
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.PriorityWithinSlot).IsRequired().HasDefaultValue(100);
        builder.Property(x => x.StateWire).HasColumnName("State").HasColumnType("text").IsRequired().HasDefaultValue("draft");
        builder.Property(x => x.VendorId);
        builder.Property(x => x.OwnerActorId).IsRequired();
        builder.Property(x => x.OwnershipOrphaned).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.LastStaleAlertAtUtc);
        builder.Property(x => x.LastStaleAlertDismissedAtUtc);
        builder.Property(x => x.CreatedAtUtc).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.EditorSaveAtUtc).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.PublishedAtUtc);
        builder.Property(x => x.ArchivedAtUtc);
        builder.Property(x => x.ArchiveReasonNote).HasColumnType("text");
        builder.Property(x => x.Xmin).IsRowVersion().HasColumnName("xmin");

        builder.HasIndex(x => new { x.StateWire, x.MarketCode, x.SlotKindWire, x.PriorityWithinSlot, x.CreatedAtUtc })
            .HasDatabaseName("IX_cms_banners_storefront_read");
        builder.HasIndex(x => new { x.StateWire, x.ScheduledStartUtc })
            .HasDatabaseName("IX_cms_banners_worker_start_scan");
        builder.HasIndex(x => new { x.StateWire, x.ScheduledEndUtc })
            .HasDatabaseName("IX_cms_banners_worker_end_scan");
        builder.HasIndex(x => new { x.OwnerActorId, x.StateWire })
            .HasDatabaseName("IX_cms_banners_owner_state");
        builder.HasIndex(x => x.VendorId)
            .HasDatabaseName("IX_cms_banners_vendor")
            .HasFilter("\"VendorId\" IS NOT NULL");
    }
}
