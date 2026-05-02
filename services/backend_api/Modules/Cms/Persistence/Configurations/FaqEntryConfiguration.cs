using BackendApi.Modules.Cms.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Cms.Persistence.Configurations;

public sealed class FaqEntryConfiguration : IEntityTypeConfiguration<FaqEntry>
{
    public void Configure(EntityTypeBuilder<FaqEntry> builder)
    {
        builder.ToTable("faq_entries", "cms", t =>
        {
            t.HasCheckConstraint("CK_cms_faq_category",
                "\"Category\" IN ('ordering','payment','shipping','returns','account','verification','b2b','other')");
            t.HasCheckConstraint("CK_cms_faq_market_code",
                "\"MarketCode\" IN ('EG','KSA','*')");
            t.HasCheckConstraint("CK_cms_faq_state",
                "\"State\" IN ('draft','scheduled','live','archived')");
            t.HasCheckConstraint("CK_cms_faq_question_ar_len",
                "\"QuestionAr\" IS NULL OR char_length(\"QuestionAr\") <= 250");
            t.HasCheckConstraint("CK_cms_faq_question_en_len",
                "\"QuestionEn\" IS NULL OR char_length(\"QuestionEn\") <= 250");
            t.HasCheckConstraint("CK_cms_faq_answer_ar_len",
                "\"AnswerAr\" IS NULL OR char_length(\"AnswerAr\") <= 4000");
            t.HasCheckConstraint("CK_cms_faq_answer_en_len",
                "\"AnswerEn\" IS NULL OR char_length(\"AnswerEn\") <= 4000");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.CategoryWire).HasColumnName("Category").HasColumnType("text").IsRequired();
        builder.Property(x => x.QuestionAr).HasColumnType("text");
        builder.Property(x => x.QuestionEn).HasColumnType("text");
        builder.Property(x => x.AnswerAr).HasColumnType("text");
        builder.Property(x => x.AnswerEn).HasColumnType("text");
        builder.Property(x => x.DisplayOrder).IsRequired().HasDefaultValue(100);
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.StateWire).HasColumnName("State").HasColumnType("text").IsRequired().HasDefaultValue("draft");
        builder.Property(x => x.ScheduledPublishAtUtc);
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

        builder.HasIndex(x => new { x.StateWire, x.MarketCode, x.CategoryWire, x.DisplayOrder, x.CreatedAtUtc })
            .HasDatabaseName("IX_cms_faq_storefront_read");
        builder.HasIndex(x => new { x.CategoryWire, x.MarketCode })
            .HasDatabaseName("IX_cms_faq_admin_grouping");
    }
}
