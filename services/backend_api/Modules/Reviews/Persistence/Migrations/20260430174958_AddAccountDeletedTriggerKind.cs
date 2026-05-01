using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Reviews.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountDeletedTriggerKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_reviews_triggered_by",
                schema: "reviews",
                table: "reviews");

            migrationBuilder.AddCheckConstraint(
                name: "CK_reviews_triggered_by",
                schema: "reviews",
                table: "reviews",
                sql: "\"TriggeredBy\" IN ('customer_submission','customer_edit','community_report_threshold','refund_event','account_locked','account_deleted','moderator_action','manual_super_admin')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_reviews_triggered_by",
                schema: "reviews",
                table: "reviews");

            migrationBuilder.AddCheckConstraint(
                name: "CK_reviews_triggered_by",
                schema: "reviews",
                table: "reviews",
                sql: "\"TriggeredBy\" IN ('customer_submission','customer_edit','community_report_threshold','refund_event','account_locked','moderator_action','manual_super_admin')");
        }
    }
}
