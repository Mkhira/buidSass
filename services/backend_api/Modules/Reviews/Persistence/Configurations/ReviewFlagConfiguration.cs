using BackendApi.Modules.Reviews.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Reviews.Persistence.Configurations;

public sealed class ReviewFlagConfiguration : IEntityTypeConfiguration<ReviewFlag>
{
    public void Configure(EntityTypeBuilder<ReviewFlag> builder)
    {
        builder.ToTable("review_flags", "reviews", t =>
        {
            t.HasCheckConstraint("CK_review_flags_reason",
                "\"Reason\" IN ('inappropriate_language','spam_or_irrelevant','personal_attack','false_or_misleading','other_with_required_note')");
            t.HasCheckConstraint("CK_review_flags_other_note_required",
                "\"Reason\" <> 'other_with_required_note' OR (\"Note\" IS NOT NULL AND char_length(\"Note\") >= 10)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ReviewId).IsRequired();
        builder.Property(x => x.ReporterActorId).IsRequired();
        builder.Property(x => x.Reason).HasColumnType("text").IsRequired();
        builder.Property(x => x.Note).HasColumnType("text");
        builder.Property(x => x.IsQualified).IsRequired();
        builder.Property(x => x.QualifyingEvaluationJson)
            .HasColumnType("jsonb")
            .HasColumnName("QualifyingEvaluation")
            .IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.ReviewId, x.ReporterActorId })
            .IsUnique()
            .HasDatabaseName("UX_review_flags_review_reporter");

        builder.HasIndex(x => new { x.ReviewId, x.IsQualified, x.CreatedAtUtc })
            .HasDatabaseName("IX_review_flags_review_qualified_recent");

        builder.HasIndex(x => new { x.ReporterActorId, x.CreatedAtUtc })
            .HasDatabaseName("IX_review_flags_reporter_created");

        builder.HasOne<Review>()
            .WithMany()
            .HasForeignKey(x => x.ReviewId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
