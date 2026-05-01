using BackendApi.Modules.Reviews.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Reviews.Persistence.Configurations;

public sealed class ProductRatingAggregateConfiguration : IEntityTypeConfiguration<ProductRatingAggregate>
{
    public void Configure(EntityTypeBuilder<ProductRatingAggregate> builder)
    {
        builder.ToTable("product_rating_aggregates", "reviews", t =>
        {
            t.HasCheckConstraint("CK_pra_market_code", "\"MarketCode\" IN ('SA','EG')");
            // Aggregate invariants per data-model §2.5 / FR-028:
            //   • Per-bucket counts MUST be non-negative.
            //   • ReviewCount MUST equal sum of buckets when set.
            //   • AvgRating, when present, MUST land in [1.00, 5.00].
            //   • AvgRating MUST be NULL exactly when ReviewCount = 0 (FR-028).
            t.HasCheckConstraint("CK_pra_review_count_nonneg", "\"ReviewCount\" >= 0");
            t.HasCheckConstraint("CK_pra_dist_buckets_nonneg",
                "\"Distribution1\" >= 0 AND \"Distribution2\" >= 0 AND \"Distribution3\" >= 0 AND \"Distribution4\" >= 0 AND \"Distribution5\" >= 0");
            t.HasCheckConstraint("CK_pra_avg_rating_range",
                "\"AvgRating\" IS NULL OR (\"AvgRating\" >= 1.00 AND \"AvgRating\" <= 5.00)");
            t.HasCheckConstraint("CK_pra_avg_null_iff_count_zero",
                "(\"ReviewCount\" = 0 AND \"AvgRating\" IS NULL) OR (\"ReviewCount\" > 0 AND \"AvgRating\" IS NOT NULL)");
            t.HasCheckConstraint("CK_pra_count_equals_buckets_sum",
                "\"ReviewCount\" = \"Distribution1\" + \"Distribution2\" + \"Distribution3\" + \"Distribution4\" + \"Distribution5\"");
        });

        builder.HasKey(x => new { x.ProductId, x.MarketCode });

        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.AvgRating).HasColumnType("numeric(3,2)");
        builder.Property(x => x.ReviewCount).IsRequired();
        builder.Property(x => x.Distribution1).IsRequired();
        builder.Property(x => x.Distribution2).IsRequired();
        builder.Property(x => x.Distribution3).IsRequired();
        builder.Property(x => x.Distribution4).IsRequired();
        builder.Property(x => x.Distribution5).IsRequired();
        builder.Property(x => x.LastUpdatedUtc).IsRequired();
        builder.Property(x => x.VendorId);

        builder.HasIndex(x => x.LastUpdatedUtc)
            .HasDatabaseName("IX_pra_last_updated");

        builder.HasIndex(x => x.VendorId)
            .HasDatabaseName("IX_pra_vendor")
            .HasFilter("\"VendorId\" IS NOT NULL");
    }
}
