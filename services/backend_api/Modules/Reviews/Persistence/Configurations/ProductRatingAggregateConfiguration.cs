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
