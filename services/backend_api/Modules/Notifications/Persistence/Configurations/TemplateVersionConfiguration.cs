using BackendApi.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Notifications.Persistence.Configurations;

public sealed class TemplateVersionConfiguration : IEntityTypeConfiguration<TemplateVersion>
{
    public void Configure(EntityTypeBuilder<TemplateVersion> builder)
    {
        builder.ToTable("template_versions", "notifications", t =>
        {
            t.HasCheckConstraint("CK_template_versions_state",
                @"""State"" IN ('draft','in_review','published','archived')");
            // V-1 publish gate: published rows must have ar_editorial_reviewed=true and
            // a reviewer != author.
            t.HasCheckConstraint("CK_template_versions_publish_gate",
                @"(""State"" NOT IN ('published','archived')) OR (""ArEditorialReviewed"" = true AND ""ReviewerId"" IS NOT NULL AND ""ReviewerId"" <> ""AuthorId"" AND ""PublishedAt"" IS NOT NULL)");
            // Locale completeness (Principle 4).
            t.HasCheckConstraint("CK_template_versions_locale_complete",
                @"(""State"" = 'draft') OR (length(""BodyAr"") > 0 AND length(""BodyEn"") > 0)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.TemplateId).IsRequired();
        builder.Property(x => x.VersionNo).IsRequired();
        builder.Property(x => x.State).HasColumnType("text").IsRequired();
        builder.Property(x => x.BodyAr).HasColumnType("text").IsRequired();
        builder.Property(x => x.BodyEn).HasColumnType("text").IsRequired();
        builder.Property(x => x.SubjectAr).HasColumnType("text");
        builder.Property(x => x.SubjectEn).HasColumnType("text");
        builder.Property(x => x.PlaceholdersJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ArEditorialReviewed).IsRequired();
        builder.Property(x => x.AuthorId).IsRequired();
        builder.Property(x => x.ReviewerId);
        builder.Property(x => x.ReviewerComment).HasColumnType("text");
        builder.Property(x => x.SubmittedAt);
        builder.Property(x => x.PublishedAt);
        builder.Property(x => x.ArchivedAt);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => new { x.TemplateId, x.VersionNo })
            .IsUnique()
            .HasDatabaseName("UX_template_versions_template_version");

        builder.HasIndex(x => new { x.TemplateId, x.State })
            .HasDatabaseName("IX_template_versions_template_state");

        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}
