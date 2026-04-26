using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Returns.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Returns_DeepReviewFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_returns_return_lines_inspection_qty_balance",
                schema: "returns",
                table: "return_lines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_returns_return_lines_received_qty_bounds",
                schema: "returns",
                table: "return_lines");

            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "returns",
                table: "returns_outbox",
                type: "citext",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "returns",
                table: "return_state_transitions",
                type: "citext",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "returns",
                table: "return_lines",
                type: "citext",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "returns",
                table: "refunds",
                type: "citext",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "returns",
                table: "refund_lines",
                type: "citext",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "returns",
                table: "inspections",
                type: "citext",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "returns",
                table: "inspection_lines",
                type: "citext",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_returns_outbox_pending_per_market",
                schema: "returns",
                table: "returns_outbox",
                columns: new[] { "MarketCode", "CommittedAt" },
                filter: "\"DispatchedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_returns_state_transitions_market_occurred",
                schema: "returns",
                table: "return_state_transitions",
                columns: new[] { "MarketCode", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_returns_return_lines_market_order_line",
                schema: "returns",
                table: "return_lines",
                columns: new[] { "MarketCode", "OrderLineId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_returns_return_lines_inspection_qty_balance",
                schema: "returns",
                table: "return_lines",
                sql: "(\"SellableQty\" IS NULL AND \"DefectiveQty\" IS NULL) OR (\"ReceivedQty\" IS NOT NULL AND \"SellableQty\" IS NOT NULL AND \"DefectiveQty\" IS NOT NULL AND \"SellableQty\" + \"DefectiveQty\" = \"ReceivedQty\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_returns_return_lines_received_qty_bounds",
                schema: "returns",
                table: "return_lines",
                sql: "\"ReceivedQty\" IS NULL OR (\"ApprovedQty\" IS NOT NULL AND \"ReceivedQty\" >= 0 AND \"ReceivedQty\" <= \"ApprovedQty\")");

            migrationBuilder.CreateIndex(
                name: "IX_returns_refunds_market_state",
                schema: "returns",
                table: "refunds",
                columns: new[] { "MarketCode", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_returns_inspections_market_started",
                schema: "returns",
                table: "inspections",
                columns: new[] { "MarketCode", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_returns_outbox_pending_per_market",
                schema: "returns",
                table: "returns_outbox");

            migrationBuilder.DropIndex(
                name: "IX_returns_state_transitions_market_occurred",
                schema: "returns",
                table: "return_state_transitions");

            migrationBuilder.DropIndex(
                name: "IX_returns_return_lines_market_order_line",
                schema: "returns",
                table: "return_lines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_returns_return_lines_inspection_qty_balance",
                schema: "returns",
                table: "return_lines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_returns_return_lines_received_qty_bounds",
                schema: "returns",
                table: "return_lines");

            migrationBuilder.DropIndex(
                name: "IX_returns_refunds_market_state",
                schema: "returns",
                table: "refunds");

            migrationBuilder.DropIndex(
                name: "IX_returns_inspections_market_started",
                schema: "returns",
                table: "inspections");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "returns",
                table: "returns_outbox");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "returns",
                table: "return_state_transitions");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "returns",
                table: "return_lines");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "returns",
                table: "refunds");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "returns",
                table: "refund_lines");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "returns",
                table: "inspections");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "returns",
                table: "inspection_lines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_returns_return_lines_inspection_qty_balance",
                schema: "returns",
                table: "return_lines",
                sql: "(\"SellableQty\" IS NULL AND \"DefectiveQty\" IS NULL) OR (\"SellableQty\" + \"DefectiveQty\" = \"ReceivedQty\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_returns_return_lines_received_qty_bounds",
                schema: "returns",
                table: "return_lines",
                sql: "\"ReceivedQty\" IS NULL OR (\"ReceivedQty\" >= 0 AND \"ReceivedQty\" <= \"ApprovedQty\")");
        }
    }
}
