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

            // Once Up() shipped, a customer may legitimately hold one
            // active review per market for the same product. Recreating
            // the legacy 2-column unique index would then fail with a
            // duplicate-key violation. Pre-check and abort with a
            // descriptive error so the operator can decide how to
            // reconcile (manual merge / soft-delete / accept data loss)
            // instead of getting a generic 23505.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    v_dup_count integer;
BEGIN
    SELECT COUNT(*) INTO v_dup_count
    FROM (
        SELECT ""CustomerId"", ""ProductId""
          FROM reviews.reviews
         WHERE ""State"" <> 'deleted'
         GROUP BY ""CustomerId"", ""ProductId""
        HAVING COUNT(*) > 1
    ) AS dups;

    IF v_dup_count > 0 THEN
        RAISE EXCEPTION
            'Cannot roll back UX_reviews_customer_product_active to '
            '(CustomerId, ProductId): % cross-market duplicate group(s) '
            'exist. Reconcile (merge or soft-delete) before retrying.',
            v_dup_count;
    END IF;
END
$$;");

            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ""UX_reviews_customer_product_active""
    ON reviews.reviews (""CustomerId"", ""ProductId"")
    WHERE ""State"" <> 'deleted';");
        }
    }
}
