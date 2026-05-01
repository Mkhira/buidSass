using BackendApi.Modules.Reviews.Entities;
using BackendApi.Modules.Reviews.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BackendApi.Modules.Reviews.Persistence.Configurations;

public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews", "reviews", t =>
        {
            t.HasCheckConstraint("CK_reviews_market_code", "\"MarketCode\" IN ('SA','EG')");
            t.HasCheckConstraint("CK_reviews_rating_range", "\"Rating\" BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_reviews_headline_len", "char_length(\"Headline\") BETWEEN 1 AND 100");
            t.HasCheckConstraint("CK_reviews_body_len", "char_length(\"Body\") BETWEEN 1 AND 4000");
            t.HasCheckConstraint("CK_reviews_locale", "\"Locale\" IN ('ar','en')");
            t.HasCheckConstraint("CK_reviews_state",
                "\"State\" IN ('pending_moderation','visible','flagged','hidden','deleted')");
            t.HasCheckConstraint("CK_reviews_triggered_by",
                "\"TriggeredBy\" IN ('customer_submission','customer_edit','community_report_threshold','refund_event','account_locked','account_deleted','moderator_action','manual_super_admin')");
            t.HasCheckConstraint("CK_reviews_media_max",
                "jsonb_array_length(\"MediaUrls\") <= 4");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.OrderLineId).IsRequired();
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.Rating).IsRequired();
        builder.Property(x => x.Headline).HasColumnType("text").IsRequired();
        builder.Property(x => x.Body).HasColumnType("text").IsRequired();
        builder.Property(x => x.Locale).HasColumnType("text").IsRequired();
        builder.Property(x => x.MediaUrlsJson)
            .HasColumnType("jsonb")
            .HasColumnName("MediaUrls")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property(x => x.State)
            .HasConversion(new ValueConverter<ReviewState, string>(
                v => ToWire(v),
                s => FromWire(s)))
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.StateChangedAtUtc).IsRequired();
        builder.Property(x => x.StateChangedByActorId).IsRequired();
        builder.Property(x => x.StateChangedReasonNote).HasColumnType("text");
        builder.Property(x => x.StateChangedAdminNote).HasColumnType("text");
        builder.Property(x => x.TriggeredBy).HasColumnType("text").IsRequired();

        builder.Property(x => x.PendingModerationStartedAt);
        builder.Property(x => x.FilterTripTerms)
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired();
        builder.Property(x => x.MediaAttachmentReviewRequired).IsRequired();

        builder.Property(x => x.EditCount).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.DeliveredAtUtc).IsRequired();
        builder.Property(x => x.VendorId);
        builder.Property(x => x.Xmin).IsRowVersion().HasColumnName("xmin");

        // §2.1 indexes
        builder.HasIndex(x => x.State)
            .HasDatabaseName("IX_reviews_state")
            .HasFilter("\"State\" IN ('pending_moderation','flagged')");

        builder.HasIndex(x => new { x.ProductId, x.MarketCode, x.State })
            .HasDatabaseName("IX_reviews_product_market_state");

        builder.HasIndex(x => new { x.CustomerId, x.State })
            .HasDatabaseName("IX_reviews_customer_state");

        builder.HasIndex(x => new { x.MarketCode, x.PendingModerationStartedAt })
            .HasDatabaseName("IX_reviews_market_pending_age")
            .HasFilter("\"State\" = 'pending_moderation'");

        builder.HasIndex(x => x.VendorId)
            .HasDatabaseName("IX_reviews_vendor")
            .HasFilter("\"VendorId\" IS NOT NULL");

        // Unique-partial enforcing FR-008 — created via raw SQL in the migration
        // because EF cannot model a unique index whose filter references the
        // ENUM-typed State column reliably across providers.
    }

    private static string ToWire(ReviewState state) => state switch
    {
        ReviewState.PendingModeration => "pending_moderation",
        ReviewState.Visible => "visible",
        ReviewState.Flagged => "flagged",
        ReviewState.Hidden => "hidden",
        ReviewState.Deleted => "deleted",
        _ => throw new InvalidOperationException($"Unknown ReviewState: {state}"),
    };

    private static ReviewState FromWire(string s) => s switch
    {
        "pending_moderation" => ReviewState.PendingModeration,
        "visible" => ReviewState.Visible,
        "flagged" => ReviewState.Flagged,
        "hidden" => ReviewState.Hidden,
        "deleted" => ReviewState.Deleted,
        _ => throw new InvalidOperationException($"Unknown ReviewState wire value: '{s}'."),
    };
}
