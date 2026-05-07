using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Reviews.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TightenActiveReviewUniqueIndexOnMarket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-010 follow-through. The init migration created the active-
            // review uniqueness key as `(CustomerId, ProductId)` only, which
            // accidentally blocks a customer from posting reviews on the
            // same product across different markets. Tighten the partial
            // unique index to include `MarketCode` so cross-market
            // partitioning is enforced at the index level — matching the
            // market-scoped LINQ precheck in SubmitReviewHandler.
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS reviews.""UX_reviews_customer_product_active"";");
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ""UX_reviews_customer_product_active""
    ON reviews.reviews (""CustomerId"", ""ProductId"", ""MarketCode"")
    WHERE ""State"" <> 'deleted';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS reviews.""UX_reviews_customer_product_active"";");
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ""UX_reviews_customer_product_active""
    ON reviews.reviews (""CustomerId"", ""ProductId"")
    WHERE ""State"" <> 'deleted';");
        }
    }
}
