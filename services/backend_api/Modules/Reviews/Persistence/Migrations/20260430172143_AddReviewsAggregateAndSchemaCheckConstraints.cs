using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Reviews.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewsAggregateAndSchemaCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_rms_community_report_window",
                schema: "reviews",
                table: "reviews_market_schemas",
                sql: "\"CommunityReportWindowDays\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_rms_pending_moderation_sla",
                schema: "reviews",
                table: "reviews_market_schemas",
                sql: "\"PendingModerationSlaHours\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_pra_avg_null_iff_count_zero",
                schema: "reviews",
                table: "product_rating_aggregates",
                sql: "(\"ReviewCount\" = 0 AND \"AvgRating\" IS NULL) OR (\"ReviewCount\" > 0 AND \"AvgRating\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_pra_avg_rating_range",
                schema: "reviews",
                table: "product_rating_aggregates",
                sql: "\"AvgRating\" IS NULL OR (\"AvgRating\" >= 1.00 AND \"AvgRating\" <= 5.00)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_pra_count_equals_buckets_sum",
                schema: "reviews",
                table: "product_rating_aggregates",
                sql: "\"ReviewCount\" = \"Distribution1\" + \"Distribution2\" + \"Distribution3\" + \"Distribution4\" + \"Distribution5\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_pra_dist_buckets_nonneg",
                schema: "reviews",
                table: "product_rating_aggregates",
                sql: "\"Distribution1\" >= 0 AND \"Distribution2\" >= 0 AND \"Distribution3\" >= 0 AND \"Distribution4\" >= 0 AND \"Distribution5\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_pra_review_count_nonneg",
                schema: "reviews",
                table: "product_rating_aggregates",
                sql: "\"ReviewCount\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_rms_community_report_window",
                schema: "reviews",
                table: "reviews_market_schemas");

            migrationBuilder.DropCheckConstraint(
                name: "CK_rms_pending_moderation_sla",
                schema: "reviews",
                table: "reviews_market_schemas");

            migrationBuilder.DropCheckConstraint(
                name: "CK_pra_avg_null_iff_count_zero",
                schema: "reviews",
                table: "product_rating_aggregates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_pra_avg_rating_range",
                schema: "reviews",
                table: "product_rating_aggregates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_pra_count_equals_buckets_sum",
                schema: "reviews",
                table: "product_rating_aggregates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_pra_dist_buckets_nonneg",
                schema: "reviews",
                table: "product_rating_aggregates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_pra_review_count_nonneg",
                schema: "reviews",
                table: "product_rating_aggregates");
        }
    }
}
