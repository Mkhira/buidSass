using BackendApi.Modules.Reviews.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Reviews.Persistence.Configurations;

public sealed class ReviewAdminNoteConfiguration : IEntityTypeConfiguration<ReviewAdminNote>
{
    public void Configure(EntityTypeBuilder<ReviewAdminNote> builder)
    {
        builder.ToTable("review_admin_notes", "reviews", t =>
        {
            t.HasCheckConstraint("CK_review_admin_notes_len",
                "char_length(\"Note\") BETWEEN 1 AND 4000");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ReviewId).IsRequired();
        builder.Property(x => x.ActorId).IsRequired();
        builder.Property(x => x.Note).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.ReviewId, x.CreatedAtUtc })
            .HasDatabaseName("IX_review_admin_notes_review_created");

        builder.HasOne<Review>()
            .WithMany()
            .HasForeignKey(x => x.ReviewId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
