using BackendApi.Modules.Cms.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Cms.Persistence.Configurations;

public sealed class BlogArticleConfiguration : IEntityTypeConfiguration<BlogArticle>
{
    public void Configure(EntityTypeBuilder<BlogArticle> builder)
    {
        builder.ToTable("blog_articles", "cms", t =>
        {
            t.HasCheckConstraint("CK_cms_blog_category",
                "\"Category\" IN ('tips','news','guides','case_studies','clinical','other')");
            t.HasCheckConstraint("CK_cms_blog_authored_locale",
                "\"AuthoredLocale\" IN ('ar','en')");
            t.HasCheckConstraint("CK_cms_blog_market_code",
                "\"MarketCode\" IN ('EG','KSA','*')");
            t.HasCheckConstraint("CK_cms_blog_state",
                "\"State\" IN ('draft','scheduled','live','archived')");
            t.HasCheckConstraint("CK_cms_blog_seo_kind",
                "\"SeoSchemaOrgKind\" IN ('Article','BlogPosting','NewsArticle','FAQPage')");
            t.HasCheckConstraint("CK_cms_blog_body_len",
                "\"Body\" IS NULL OR char_length(\"Body\") <= 60000");
            t.HasCheckConstraint("CK_cms_blog_seo_meta_title_len",
                "\"SeoMetaTitle\" IS NULL OR char_length(\"SeoMetaTitle\") <= 70");
            t.HasCheckConstraint("CK_cms_blog_seo_meta_description_len",
                "\"SeoMetaDescription\" IS NULL OR char_length(\"SeoMetaDescription\") <= 160");
            t.HasCheckConstraint("CK_cms_blog_slug_pattern",
                "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.CategoryWire).HasColumnName("Category").HasColumnType("text").IsRequired();
        builder.Property(x => x.Slug).HasColumnType("text").IsRequired();
        builder.Property(x => x.AuthoredLocale).HasColumnType("text").IsRequired();
        builder.Property(x => x.Title).HasColumnType("text").IsRequired();
        builder.Property(x => x.Summary).HasColumnType("text");
        builder.Property(x => x.Body).HasColumnType("text");
        builder.Property(x => x.CoverAssetId);
        builder.Property(x => x.SeoMetaTitle).HasColumnType("text");
        builder.Property(x => x.SeoMetaDescription).HasColumnType("text");
        builder.Property(x => x.SeoOgImageId);
        builder.Property(x => x.SeoSchemaOrgKind).HasColumnType("text").IsRequired().HasDefaultValue("BlogPosting");
        builder.Property(x => x.ScheduledPublishAtUtc);
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
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

        builder.HasIndex(x => new { x.MarketCode, x.AuthoredLocale, x.Slug })
            .HasDatabaseName("UX_cms_blog_slug_market_locale")
            .IsUnique();
        builder.HasIndex(x => new { x.StateWire, x.MarketCode, x.CategoryWire, x.PublishedAtUtc })
            .HasDatabaseName("IX_cms_blog_storefront_read");
        builder.HasIndex(x => new { x.StateWire, x.ScheduledPublishAtUtc })
            .HasDatabaseName("IX_cms_blog_worker_scan");
    }
}
