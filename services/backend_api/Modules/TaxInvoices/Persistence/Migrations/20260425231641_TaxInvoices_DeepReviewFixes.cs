using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.TaxInvoices.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxInvoices_DeepReviewFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_invoices_invoices_order",
                schema: "invoices",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "IX_invoices_render_jobs_pending",
                schema: "invoices",
                table: "invoice_render_jobs");

            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "invoices",
                table: "invoices_outbox",
                type: "citext",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "invoices",
                table: "invoice_render_jobs",
                type: "citext",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "invoices",
                table: "invoice_lines",
                type: "citext",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "invoices",
                table: "credit_notes",
                type: "citext",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "invoices",
                table: "credit_note_lines",
                type: "citext",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_outbox_market_committed",
                schema: "invoices",
                table: "invoices_outbox",
                columns: new[] { "MarketCode", "CommittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_invoices_order",
                schema: "invoices",
                table: "invoices",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_render_jobs_market_attempt",
                schema: "invoices",
                table: "invoice_render_jobs",
                columns: new[] { "MarketCode", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_render_jobs_pending",
                schema: "invoices",
                table: "invoice_render_jobs",
                columns: new[] { "State", "NextAttemptAt" },
                filter: "\"State\" IN ('queued','failed','rendering')");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_invoice_lines_market_invoice",
                schema: "invoices",
                table: "invoice_lines",
                columns: new[] { "MarketCode", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_credit_notes_market_issued",
                schema: "invoices",
                table: "credit_notes",
                columns: new[] { "MarketCode", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_credit_note_lines_market_cn",
                schema: "invoices",
                table: "credit_note_lines",
                columns: new[] { "MarketCode", "CreditNoteId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_invoices_outbox_market_committed",
                schema: "invoices",
                table: "invoices_outbox");

            migrationBuilder.DropIndex(
                name: "IX_invoices_invoices_order",
                schema: "invoices",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "IX_invoices_render_jobs_market_attempt",
                schema: "invoices",
                table: "invoice_render_jobs");

            migrationBuilder.DropIndex(
                name: "IX_invoices_render_jobs_pending",
                schema: "invoices",
                table: "invoice_render_jobs");

            migrationBuilder.DropIndex(
                name: "IX_invoices_invoice_lines_market_invoice",
                schema: "invoices",
                table: "invoice_lines");

            migrationBuilder.DropIndex(
                name: "IX_invoices_credit_notes_market_issued",
                schema: "invoices",
                table: "credit_notes");

            migrationBuilder.DropIndex(
                name: "IX_invoices_credit_note_lines_market_cn",
                schema: "invoices",
                table: "credit_note_lines");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "invoices",
                table: "invoices_outbox");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "invoices",
                table: "invoice_render_jobs");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "invoices",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "invoices",
                table: "credit_notes");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "invoices",
                table: "credit_note_lines");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_invoices_order",
                schema: "invoices",
                table: "invoices",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_render_jobs_pending",
                schema: "invoices",
                table: "invoice_render_jobs",
                columns: new[] { "State", "NextAttemptAt" },
                filter: "\"State\" IN ('queued','failed')");
        }
    }
}
