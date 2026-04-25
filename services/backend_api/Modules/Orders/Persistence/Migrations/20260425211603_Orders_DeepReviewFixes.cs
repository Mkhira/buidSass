using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Orders.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Orders_DeepReviewFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_orders_orders_payment_provider_txn_unique",
                schema: "orders",
                table: "orders",
                columns: new[] { "PaymentProviderId", "PaymentProviderTxnId" },
                unique: true,
                filter: "\"PaymentProviderId\" IS NOT NULL AND \"PaymentProviderTxnId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_orders_payment_provider_txn_unique",
                schema: "orders",
                table: "orders");
        }
    }
}
