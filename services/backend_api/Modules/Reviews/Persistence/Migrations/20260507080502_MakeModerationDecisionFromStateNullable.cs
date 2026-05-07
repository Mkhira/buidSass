using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Reviews.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeModerationDecisionFromStateNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "FromState",
                schema: "reviews",
                table: "review_moderation_decisions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Backfill NULL FromState rows to a valid wire value before
            // re-adding NOT NULL. The empty-string default of the prior
            // revision broke ReviewState.FromWire on read.
            migrationBuilder.Sql(
                "UPDATE reviews.review_moderation_decisions " +
                "SET \"FromState\" = 'visible' " +
                "WHERE \"FromState\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "FromState",
                schema: "reviews",
                table: "review_moderation_decisions",
                type: "text",
                nullable: false,
                defaultValue: "visible",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
